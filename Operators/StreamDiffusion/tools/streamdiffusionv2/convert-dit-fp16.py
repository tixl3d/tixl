"""Convert dit_step.onnx / cross_kv.onnx weights to fp16 (halves GPU memory, roughly doubles speed).

usage: python convert-dit-fp16.py --src <fp32 export dir> --dst <out dir> [--verify]

Inputs/outputs keep their dtypes (cache tensors are already fp16). Normalization and softmax stay fp32 through
onnxconverter-common's default op block list.
"""
import argparse
import os
import shutil

import numpy as np
import onnx
from onnxconverter_common import float16


def convert(src, dst):
    model = onnx.load(src, load_external_data=True)
    converted = float16.convert_float_to_float16(model, keep_io_types=True, min_positive_val=1e-7, max_finite_val=6e4)
    data_name = os.path.basename(dst) + ".data"
    onnx.save(converted, dst, save_as_external_data=True, all_tensors_to_one_file=True, location=data_name,
              size_threshold=1024)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--src", required=True)
    parser.add_argument("--dst", required=True)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    os.makedirs(args.dst, exist_ok=True)
    for name in ["dit_step.onnx", "cross_kv.onnx"]:
        convert(os.path.join(args.src, name), os.path.join(args.dst, name))

    for name in os.listdir(args.src):
        if name not in ("dit_step.onnx", "dit_step.onnx.data", "cross_kv.onnx", "cross_kv.onnx.data"):
            shutil.copy(os.path.join(args.src, name), os.path.join(args.dst, name))

    if args.verify:
        import onnxruntime as ort
        providers = ["CUDAExecutionProvider", "CPUExecutionProvider"]
        reference = ort.InferenceSession(os.path.join(args.src, "dit_step.onnx"), providers=providers)
        half = ort.InferenceSession(os.path.join(args.dst, "dit_step.onnx"), providers=providers)
        rng = np.random.default_rng(0)
        feeds = {}
        for meta in reference.get_inputs():
            shape = [d if isinstance(d, int) else 2 for d in meta.shape]
            if "int64" in meta.type:
                feeds[meta.name] = np.full(shape, 3, dtype=np.int64)
            elif "float16" in meta.type:
                feeds[meta.name] = rng.standard_normal(shape).astype(np.float16)
            else:
                feeds[meta.name] = rng.standard_normal(shape).astype(np.float32)
        feeds["cache_mask"] = np.zeros_like(feeds["cache_mask"])
        feeds["timestep"] = np.full_like(feeds["timestep"], 500)
        feeds["sigma"] = np.full_like(feeds["sigma"], 0.7)
        a = reference.run(["x0"], feeds)[0]
        b = half.run(["x0"], feeds)[0]
        print("x0 finite:", bool(np.isfinite(b).all()), "max abs diff:", float(np.abs(a - b).max()),
              "reference absmax:", float(np.abs(a).max()))


if __name__ == "__main__":
    main()
