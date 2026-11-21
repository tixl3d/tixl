"""Export the Wan2.1 UMT5-XXL text encoder to ONNX (bf16 weights, fp32 compute).

usage: python export-text-encoder.py --t5 <models_t5_umt5-xxl-enc-bf16.pth> --out <dir> [--verify]

Sequence length is dynamic (8..512); the caller pads to a convenient length. The host must zero the output rows beyond the real
sequence length (the reference does this) before feeding the cross-KV model.
"""
import argparse
import os

import torch

from t5_encoder import SEQ_LEN, T5Encoder


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--t5", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    model = T5Encoder().to(torch.bfloat16).eval().requires_grad_(False)
    state = torch.load(args.t5, map_location="cpu", weights_only=True, mmap=True)
    model.load_state_dict(state)

    ids = torch.randint(1, 1000, (1, 96))
    mask = torch.ones(1, 96, dtype=torch.long)
    mask[:, 60:] = 0
    with torch.no_grad():
        reference = model(ids, mask)
        valid = slice(0, 60)
        print("finite:", bool(torch.isfinite(reference).all()), "absmax", float(reference.abs().max()))

        os.makedirs(args.out, exist_ok=True)
        path = os.path.join(args.out, "text_encoder.onnx")
        length = torch.export.Dim("length", min=8, max=SEQ_LEN)
        program = torch.onnx.export(model, (ids, mask), dynamo=True, input_names=["ids", "mask"],
                                    output_names=["embeds"], opset_version=18,
                                    dynamic_shapes={"ids": {1: length}, "mask": {1: length}})
        program.save(path, external_data=True)

    if args.verify:
        import numpy as np
        import onnxruntime as ort
        options = ort.SessionOptions()
        options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_DISABLE_ALL
        session = ort.InferenceSession(path, options, providers=["CPUExecutionProvider"])
        out = session.run(None, {"ids": ids.numpy(), "mask": mask.numpy()})[0].astype(np.float32)
        print("onnx vs torch max abs diff (valid rows):", float(np.abs(out[:, valid] - reference.numpy()[:, valid]).max()))


if __name__ == "__main__":
    main()
