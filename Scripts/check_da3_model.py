"""Structural check of a DA3 ONNX export against the DepthAnything operator's expectations.

Usage: python Scripts/check_da3_model.py <model.onnx>
"""
import sys

import onnx  # noqa: E402  (user site-packages)

ALIASES = {
    "input": ["pixel_values"],
    "depth": ["depth", "predicted_depth"],
    "confidence": ["depth_conf", "conf", "confidence"],
    "sky": ["sky"],
    "intrinsics": ["intrinsics"],
    "extrinsics": ["extrinsics"],
}


def dims(t):
    return [d.dim_value if d.HasField("dim_value") else d.dim_param for d in t.shape.dim]


def main():
    path = sys.argv[1]
    m = onnx.load(path, load_external_data=False)
    g = m.graph
    print("== INPUTS ==")
    for i in g.input:
        t = i.type.tensor_type
        print(" ", i.name, "elem_type", t.elem_type, dims(t))
    print("== OUTPUTS ==")
    for o in g.output:
        t = o.type.tensor_type
        print(" ", o.name, "elem_type", t.elem_type, dims(t))
    print("== MATCH ==")
    in_names = {i.name for i in g.input}
    out_names = {o.name for o in g.output}
    for role, aliases in ALIASES.items():
        hit = next((a for a in aliases if a in in_names or a in out_names), None)
        print(f"  {role:11s} -> {hit if hit else 'NONE (operator falls back)'!r}")


if __name__ == "__main__":
    main()
