"""Probe a DA3 ONNX export the way the DepthAnything operator consumes it.

Runs one CPU inference, prints output shapes/stats, and replays the operator's
exact read logic (CopyPlane / ReadVerticalFov / ReadCameraPose equivalents).

Usage: python Scripts/probe_da3_model.py <model.onnx>
"""
import sys

import numpy as np
import onnxruntime as ort

path = sys.argv[1]
sess = ort.InferenceSession(path, providers=["CPUExecutionProvider"])

inp = sess.get_inputs()[0]
print("input:", inp.name, inp.type, inp.shape)

# Build a zero input matching the model's declared shape (fixed exports) or 518 square
shape = []
for d in inp.shape:
    shape.append(d if isinstance(d, int) and d > 0 else 518)
shape[0] = 1
zeros = (np.zeros(shape, dtype=np.uint8) if "tensor(uint8)" in inp.type
         else np.zeros(shape, dtype=np.float32))
results = sess.run(None, {inp.name: zeros})

for meta, out in zip(sess.get_outputs(), results):
    flat = out.reshape(-1)
    finite = flat[np.isfinite(flat)]
    print(f"output {meta.name:12s} shape={list(out.shape)} dtype={out.dtype} "
          f"min={finite.min():.4f} max={finite.max():.4f} finite={finite.size}/{flat.size}")

# ---- operator-equivalent reads ----
out_names = [m.name for m in sess.get_outputs()]
print("\n== operator-equivalent reads ==")


def copy_plane(idx):
    """DepthAnything.CopyPlane: last two dims are H,W."""
    arr = results[idx]
    h, w = arr.shape[-2], arr.shape[-1]
    n = h * w
    if arr.dtype != np.float32:
        return None, 0, 0
    return arr.reshape(-1)[:n], w, h


depth_idx = out_names.index("depth") if "depth" in out_names else 0
conf_idx = out_names.index("depth_conf") if "depth_conf" in out_names else -1
sky_idx = out_names.index("sky") if "sky" in out_names else -1
intr_idx = out_names.index("intrinsics") if "intrinsics" in out_names else -1
extr_idx = out_names.index("extrinsics") if "extrinsics" in out_names else -1

plane, w, h = copy_plane(depth_idx)
print(f"depth plane: {w}x{h}")

if intr_idx >= 0:
    arr = results[intr_idx].reshape(-1)
    print(f"intrinsics tensor: {results[intr_idx].shape} len={arr.size}")
    if arr.size >= 9:
        fy = arr[arr.size - 9 + 4]
        fov = 2 * np.arctan(h / (2 * fy)) * 180 / np.pi
        print(f"  operator fy read = index {arr.size - 9 + 4}: fy={fy}")
        print(f"  operator VerticalFov = {fov:.2f} deg  (h={h})")
    m3 = arr[:9].reshape(3, 3)
    print("  first 3x3:", m3.tolist())

if extr_idx >= 0:
    arr = results[extr_idx]
    rows, cols = arr.shape[-2], arr.shape[-1]
    flat = arr.reshape(-1)
    print(f"extrinsics tensor: {list(arr.shape)} -> operator reads {rows}x{cols} top block")
    r = flat[:12].reshape(3, 4)
    print("  R|t:", np.round(r, 3).tolist())
    pos = -r[:, :3].T @ r[:, 3]
    print("  camera position:", np.round(pos, 3).tolist())
    fwd = r[2, :3]
    up = -r[1, :3]
    print("  forward:", np.round(fwd, 3).tolist(), "up:", np.round(up, 3).tolist())
