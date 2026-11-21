"""Export the StreamDiffusionV2 causal Wan DiT (step + cross-KV) to ONNX and check it against PyTorch.

usage: python export-dit.py --ckpt <ckpt/model.pt> --out <dir> [--height 480 --width 832] [--verify]

Writes dit_step.onnx (+ external data) and cross_kv.onnx. Resolution is baked into dit_step.onnx;
re-export for another size. Weights stay fp32; run convert-fp16.py afterwards for the fast path.
"""
import argparse
import json
import os

import torch

from wan_dit import (HEAD_DIM, LATENT_CHANNELS, NUM_HEADS, NUM_LAYERS, PATCH, TEXT_DIM, TEXT_LEN,
                     CrossKvModel, StepModel, load_core)

CACHE_FRAMES = 6
SINK_FRAMES = 3


def dummy_inputs(step_model, batch, cache_frames):
    tokens = step_model.tokens
    slots = cache_frames * tokens
    latent_h = step_model.grid_h * PATCH
    latent_w = step_model.grid_w * PATCH
    g = torch.Generator().manual_seed(0)
    mask = torch.zeros(batch, slots)
    mask[:, 3 * tokens:] = float("-inf")
    return (
        torch.randn(batch, LATENT_CHANNELS, latent_h, latent_w, generator=g),
        torch.full((batch,), 500.0),
        torch.full((batch,), 0.7),
        torch.arange(batch, dtype=torch.long) + 3,
        torch.randn(NUM_LAYERS, batch, slots, NUM_HEADS, HEAD_DIM, generator=g).half(),
        torch.randn(NUM_LAYERS, batch, slots, NUM_HEADS, HEAD_DIM, generator=g).half(),
        mask,
        torch.full((batch,), 3, dtype=torch.long),
        torch.randn(NUM_LAYERS, 1, TEXT_LEN, NUM_HEADS, HEAD_DIM, generator=g).half(),
        torch.randn(NUM_LAYERS, 1, TEXT_LEN, NUM_HEADS, HEAD_DIM, generator=g).half(),
    )


def write_zeros_model(path):
    """Graph that allocates a zeroed fp16 tensor of a given shape directly on the execution provider's device."""
    import onnx
    from onnx import TensorProto, helper

    node = helper.make_node("ConstantOfShape", ["shape"], ["zeros"],
                            value=helper.make_tensor("value", TensorProto.FLOAT16, [1], [0.0]))
    graph = helper.make_graph(
        [node], "kv_zeros",
        [helper.make_tensor_value_info("shape", TensorProto.INT64, [5])],
        [helper.make_tensor_value_info("zeros", TensorProto.FLOAT16, None)])
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 18)])
    model.ir_version = 9
    onnx.save(model, path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ckpt", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--height", type=int, default=480)
    parser.add_argument("--width", type=int, default=832)
    parser.add_argument("--batch", type=int, default=4, help="dummy batch for tracing; batch axis is dynamic")
    parser.add_argument("--fp16", action="store_true", help="fp16 weights and activations (norms stay fp32)")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    core = load_core(args.ckpt)
    dtype = torch.float16 if args.fp16 else torch.float32
    if args.fp16:
        core.half()
    latent_h = args.height // 8
    latent_w = args.width // 8
    step_model = StepModel(core, latent_h, latent_w, dtype).eval()
    inputs = dummy_inputs(step_model, args.batch, CACHE_FRAMES)

    input_names = ["latent", "timestep", "sigma", "frame_pos", "k_cache", "v_cache", "cache_mask", "slot",
                   "cross_k", "cross_v"]
    output_names = ["x0", "k_cache_out", "v_cache_out"]
    batch = torch.export.Dim("batch", min=1, max=8)
    dynamic_shapes = {
        "latent": {0: batch}, "timestep": {0: batch}, "sigma": {0: batch}, "frame_pos": {0: batch},
        "k_cache": {1: batch}, "v_cache": {1: batch}, "cache_mask": {0: batch}, "slot": {0: batch},
        "cross_k": None, "cross_v": None,
    }

    with torch.no_grad():
        reference = step_model(*inputs)
        step_path = os.path.join(args.out, "dit_step.onnx")
        program = torch.onnx.export(step_model, inputs, dynamo=True, input_names=input_names,
                                    output_names=output_names, dynamic_shapes=dynamic_shapes, opset_version=18)
        program.save(step_path, external_data=True)

        prompt = torch.randn(1, TEXT_LEN, TEXT_DIM)
        cross_model = CrossKvModel(core, dtype).eval()
        cross_reference = cross_model(prompt)
        cross_path = os.path.join(args.out, "cross_kv.onnx")
        cross_program = torch.onnx.export(cross_model, (prompt,), dynamo=True, input_names=["prompt_embeds"],
                                          output_names=["cross_k", "cross_v"], opset_version=18)
        cross_program.save(cross_path, external_data=True)

    write_zeros_model(os.path.join(args.out, "kv_zeros.onnx"))

    manifest = {
        "latentHeight": latent_h, "latentWidth": latent_w, "tokensPerFrame": step_model.tokens,
        "cacheFrames": CACHE_FRAMES, "sinkFrames": SINK_FRAMES, "layers": NUM_LAYERS,
        "heads": NUM_HEADS, "headDim": HEAD_DIM, "textLen": TEXT_LEN,
    }
    with open(os.path.join(args.out, "dit_manifest.json"), "w") as handle:
        json.dump(manifest, handle, indent=2)

    if args.verify:
        import onnxruntime as ort
        session = ort.InferenceSession(step_path, providers=["CPUExecutionProvider"])
        feeds = {name: tensor.numpy() for name, tensor in zip(input_names, inputs)}
        outputs = session.run(None, feeds)
        for name, expected, actual in zip(output_names, reference, outputs):
            diff = (expected.float().numpy() - actual.astype("float32")).max()
            print(f"{name}: max abs diff {abs(diff):.6f}")
        cross = ort.InferenceSession(cross_path, providers=["CPUExecutionProvider"])
        cross_outputs = cross.run(None, {"prompt_embeds": prompt.numpy()})
        for name, expected, actual in zip(["cross_k", "cross_v"], cross_reference, cross_outputs):
            print(f"{name}: max abs diff {abs(expected.float().numpy() - actual.astype('float32')).max():.6f}")


if __name__ == "__main__":
    main()
