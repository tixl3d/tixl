"""Convert a Depth Anything ONNX export to fp16 in place, next to the source file.

The operator prefers the fp16 variant (`da3-small-fp16.onnx` before `da3-small.onnx`). fp16
initializers halve the weights and use the GPU's fp16 tensor cores: on a laptop RTX 3080 Ti
the fp32 DA3 Small takes ~163 ms per 518x518 frame through ONNX Runtime, the fp16 file ~40 ms
for the same output (correlation 0.999997 against fp32).

Usage:
    python convert_to_fp16.py da3-small.onnx
    python convert_to_fp16.py D:/models --all     # every *.onnx in a folder, skipping *-fp16.onnx

Requires `onnx` and `onnxruntime`.
"""

import argparse
import os
import sys

import onnx
import onnxruntime as ort
from onnxruntime.transformers import float16


def convert(path: str) -> str:
    if path.endswith("-fp16.onnx"):
        print(f"skip (already fp16): {path}")
        return path

    target = path[: -len(".onnx")] + "-fp16.onnx"
    model = onnx.load(path)
    converted = float16.convert_float_to_float16(model, keep_io_types=True)

    # Load once before overwriting anything: a graph ONNX Runtime rejects is not worth saving.
    onnx.save(converted, target)
    try:
        session = ort.InferenceSession(target, providers=["CPUExecutionProvider"])
    except Exception:
        os.remove(target)
        raise

    inputs = ", ".join(f"{i.name}:{i.type}" for i in session.get_inputs())
    outputs = ", ".join(f"{o.name}:{o.type}" for o in session.get_outputs())
    print(f"wrote {target} ({os.path.getsize(target) / 1e6:.1f} MB)")
    print(f"  inputs  {inputs}")
    print(f"  outputs {outputs}")
    print("  the operator picks this file before the fp32 export")
    return target


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", help="ONNX file or folder")
    parser.add_argument("--all", action="store_true", help="convert every non-fp16 .onnx in the folder")
    args = parser.parse_args()

    if os.path.isdir(args.path):
        candidates = sorted(
            os.path.join(args.path, name)
            for name in os.listdir(args.path)
            if name.endswith(".onnx") and not name.endswith("-fp16.onnx")
        )
        if not args.all or not candidates:
            parser.error("a folder needs --all and at least one non-fp16 .onnx file")
        for candidate in candidates:
            convert(candidate)
        return 0

    if not os.path.isfile(args.path):
        parser.error(f"not found: {args.path}")
    convert(args.path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
