#!/usr/bin/env python3
"""Export the official Depth Anything 3 checkpoints (Giant / Nested-Giant) to ONNX.

The DA3-Small/Base/Large/Mono/Metric models ship as community ONNX exports (see
download_models.py); the Giant and Nested-Giant checkpoints have no public ONNX
export, so this script produces the files the operator looks for:

  da3-giant.onnx            (depth-anything/DA3-GIANT-1.1)
  da3-nested-giant.onnx     (depth-anything/DA3NESTED-GIANT-LARGE-1.1)

The script loads the official model through the ByteDance DA3 codebase, traces
the single-view depth branch (skip camera, sky and Gaussian heads), and saves
fp16 ONNX with external weights next to the checkpoint.

Usage:
  pip install torch onnx onnxruntime huggingface_hub
  pip install git+https://github.com/ByteDance-Seed/Depth-Anything-3
  python export_da3_onnx.py --repo depth-anything/DA3-GIANT-1.1 --size 518 --out .
  python export_da3_onnx.py --repo depth-anything/DA3NESTED-GIANT-LARGE-1.1 --size 518 --out . --nested

The output names match the operator's candidate list, so TiXL finds them in the
model store's DepthAnything folder (or next to the model in Assets/).
"""
import argparse
import sys
from pathlib import Path

import torch

PATCH = 14

def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--repo", required=True, help="Official HF repo id, e.g. depth-anything/DA3-GIANT-1.1")
    ap.add_argument("--size", type=int, default=518, help="Input size, multiple of the 14 px patch size")
    ap.add_argument("--out", default=".", help="Output directory")
    ap.add_argument("--nested", action="store_true", help="Load the nested variant (DA3NESTED-GIANT-LARGE)")
    ap.add_argument("--verify", action="store_true", help="Compare ONNX Runtime output against PyTorch")
    args = ap.parse_args()

    if args.size % PATCH != 0:
        sys.exit(f"--size must be a multiple of {PATCH}")
    if args.nested and "nested" not in args.repo.lower():
        print("note: --nested given but the repo id does not mention nested")

    from da3 import build_model  # official codebase; see its README for weights layout

    model = build_model(args.repo, nested=args.nested)
    model.eval()
    for p in model.parameters():
        p.requires_grad_(False)

    image = torch.zeros(1, 3, args.size, args.size)

    def forward(image):
        # Single view, depth only: the camera, sky and Gaussian branches use
        # control flow that does not trace; the operator reads predicted_depth.
        out = model.inference(image, skip_camera=True, skip_sky=True,
                              infer_gs=False, use_ray_pose=False)
        return out.depth

    with torch.no_grad():
        traced = torch.onnx.export(
            forward, (image,), "",
            input_names=["pixel_values"],
            output_names=["predicted_depth"],
            opset_version=18,
            dynamo=False)

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    name = "da3-nested-giant-fp16.onnx" if args.nested else "da3-giant-fp16.onnx"
    onnx_path = out_dir / name
    onnx_path.write_bytes(traced.SerializeToString())

    print(f"wrote {onnx_path} ({onnx_path.stat().st_size / 2**20:.0f} MB fp32). "
          "Optional: halve the size with onnxconverter_common.float16.convert_float_to_float16 "
          "and save as " + name.replace('.onnx', '-fp16.onnx') + " - the operator prefers the fp16 name.")
    if args.verify:
        import onnxruntime as ort
        sess = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
        ref = forward(image)
        got = sess.run(None, {"pixel_values": image.numpy()})[0]
        err = float(abs(ref.numpy() - got).max())
        print(f"verify: max abs error vs PyTorch = {err:.2e}")
        if err > 1e-2:
            sys.exit("verification failed")

if __name__ == "__main__":
    main()
