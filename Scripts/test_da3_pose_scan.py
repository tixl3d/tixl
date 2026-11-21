"""Ground-truth camera-motion test for the DA3 scan chain.

Renders a two-plane scene from two cameras with KNOWN relative motion (yaw + lateral
translation), runs the real ONNX model on both frames, and compares the estimated
relative pose against ground truth. Then fuses both views' depth with the estimated
poses and checks they agree on the world (the property DepthScan3D depends on).

Run: python Scripts/test_da3_pose_scan.py <model.onnx>
"""
import sys

import numpy as np
import onnxruntime as ort

rng = np.random.default_rng(7)
W = H = 504
FX = 700.0

# ---------- ground-truth scene: near card + far wall ----------
tex_near = rng.integers(0, 255, (40, 60, 3), dtype=np.uint8).repeat(16, 0).repeat(16, 1)[:512, :768]
tex_far = rng.integers(0, 255, (30, 45, 3), dtype=np.uint8).repeat(16, 0).repeat(16, 1)[:504, :504]
Z_NEAR, Z_FAR = 0.6, 2.5
CARD_HALF = 0.18

def render(tex, R, t, plane_z_world, extent):
    """Ray-cast a textured world plane (z = plane_z_world) into a virtual camera."""
    yy, xx = np.mgrid[0:H, 0:W]
    d_cam = np.stack([(xx - W / 2) / FX, (yy - H / 2) / FX,
                      np.ones_like(xx, float)], -1).reshape(-1, 3)
    d_world = d_cam @ np.linalg.inv(R).T
    o_world = -(R.T @ t)
    s = (plane_z_world - o_world[2]) / d_world[:, 2]
    pt = o_world + s[:, None] * d_world
    inside = (s > 0) & (np.abs(pt[:, 0]) <= extent[0]) & (np.abs(pt[:, 1]) <= extent[1])
    hw, hh = extent
    u = (pt[:, 0] / (2 * hw) + 0.5) * (tex.shape[1] - 1)
    v = (pt[:, 1] / (2 * hh) + 0.5) * (tex.shape[0] - 1)
    ui = np.round(u).astype(int).clip(0, tex.shape[1] - 1)
    vi = np.round(v).astype(int).clip(0, tex.shape[0] - 1)
    out = np.zeros((H * W, 3), np.uint8)
    out[inside] = tex[vi[inside], ui[inside]]
    return out.reshape(H, W, 3), inside.reshape(H, W)

def rot(axis, deg):
    a = np.radians(deg)
    c, s = np.cos(a), np.sin(a)
    if axis == "y":
        return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])

R_A, t_A = np.eye(3), np.zeros(3)
R_B = rot("y", 4) @ rot("x", 2)
t_B = np.array([0.06, 0.02, 0.0])

img_a = render(tex_far, R_A, t_A, Z_FAR, (3.0, 3.0))[0]
near_a, mask_a = render(tex_near, R_A, t_A, Z_NEAR, (CARD_HALF, CARD_HALF))
img_a = np.where(mask_a[..., None], near_a, img_a)
img_b = render(tex_far, R_B, t_B, Z_FAR, (3.0, 3.0))[0]
near_b, mask_b = render(tex_near, R_B, t_B, Z_NEAR, (CARD_HALF, CARD_HALF))
img_b = np.where(mask_b[..., None], near_b, img_b)

# ---------- run the real model ----------
sess = ort.InferenceSession(sys.argv[1], providers=["CPUExecutionProvider"])
inp_name = sess.get_inputs()[0].name
out_names = [o.name for o in sess.get_outputs()]

def preprocess(img):
    x = (img.astype(np.float32) / 255.0 - np.array([0.485, 0.456, 0.406], np.float32)) \
        / np.array([0.229, 0.224, 0.225], np.float32)
    return np.ascontiguousarray(x.transpose(2, 0, 1)[None].astype(np.float32))

def infer(img):
    outs = sess.run(None, {inp_name: preprocess(img)})
    by = dict(zip(out_names, outs))
    d = by["depth"].astype(np.float32).reshape(H, W)
    conf = by["depth_conf"].astype(np.float32).reshape(H, W)
    E = by["extrinsics"].reshape(3, 4)
    K = by["intrinsics"].reshape(3, 3)
    return d, conf, E, K

d_a, c_a, E_a, K_a = infer(img_a)
d_b, c_b, E_b, K_b = infer(img_b)

def pose_of(E):
    R, t = E[:, :3], E[:, 3]
    return -R.T @ t, R[2, :3]

pos_a, fwd_a = pose_of(E_a)
pos_b, fwd_b = pose_of(E_b)
R_rel = E_b[:, :3] @ E_a[:, :3].T
t_rel = t_B - R_rel @ t_A
cos_dir = float((t_rel / (np.linalg.norm(t_rel) + 1e-9)) @ (t_B / np.linalg.norm(t_B)))
rot_est = np.degrees(np.arccos(np.clip((np.trace(R_rel) - 1) / 2, -1, 1)))

print("== T2: pose vs ground truth ==")
print("GT motion: yaw 4 deg, pitch 2 deg, translation [0.06, 0.02, 0] (near plane at z=0.6)")
print(f"estimated relative rotation: {rot_est:.2f} deg (GT 4.47)")
print(f"translation direction cosine(est, gt): {cos_dir:+.3f} (1 = perfect)")
print(f"estimated |t| in relative units: {np.linalg.norm(t_rel):.4f}  (GT 0.0632 world units)")

print("\n== T3: fusion consistency ==")
u, v = np.meshgrid(np.arange(W), np.arange(H))
thr = np.median(c_a)
m = c_a >= thr
xs = (u - K_a[0, 2]) / K_a[0, 0] * d_a
ys = (v - K_a[1, 2]) / K_a[1, 1] * d_a
pA = np.stack([xs[m], ys[m], d_a[m]], -1)
world = (np.linalg.inv(E_a[:, :3]) @ (pA - E_a[:, 3]).T).T

Rb, tb = E_b[:, :3], E_b[:, 3]
pB = world @ Rb.T + tb
z_proj = pB[:, 2]
u_b = K_b[0, 0] * pB[:, 0] / np.maximum(z_proj, 1e-6) + K_b[0, 2]
v_b = K_b[1, 1] * pB[:, 1] / np.maximum(z_proj, 1e-6) + K_b[1, 2]
valid = (u_b >= 0) & (u_b < W - 1) & (v_b >= 0) & (v_b < H - 1) & (z_proj > 1e-4)
ui, vi = u_b[valid].astype(int), v_b[valid].astype(int)
z_model_b = d_b[vi, ui]
rel = np.abs(z_proj[valid] - z_model_b) / np.maximum(np.abs(z_model_b), 1e-6)
print(f"points reprojected: {int(valid.sum())}")
print(f"median relative depth error with estimated poses: {np.median(rel)*100:.2f}%")

pB_wrong = world @ E_a[:, :3].T + E_a[:, 3]
zw = pB_wrong[:, 2]
u_w = K_b[0, 0] * pB_wrong[:, 0] / np.maximum(zw, 1e-6) + K_b[0, 2]
v_w = K_b[1, 1] * pB_wrong[:, 1] / np.maximum(zw, 1e-6) + K_b[1, 2]
val_w = (u_w >= 0) & (u_w < W - 1) & (v_w >= 0) & (v_w < H - 1) & (zw > 1e-4)
wi, hi = u_w[val_w].astype(int), v_w[val_w].astype(int)
rel_wrong = np.abs(zw[val_w] - d_b[hi, wi]) / np.maximum(np.abs(d_b[hi, wi]), 1e-6)
print(f"median relative depth error with static pose (control): {np.median(rel_wrong)*100:.2f}%")

vox = 0.04
def q(pts):
    return {tuple(np.round(p / vox).astype(np.int64)) for p in pts}

world_b = (np.linalg.inv(E_b[:, :3]) @ ((np.stack([(u - K_b[0, 2]) / K_b[0, 0] * d_b,
                                                   (v - K_b[1, 2]) / K_b[1, 1] * d_b,
                                                   d_b], -1).reshape(-1, 3)) - E_b[:, 3]).T).T
va, vb = q(world), q(world_b)
shared = len(va & vb)
print(f"\n== voxel fusion (voxel={vox}) ==")
print(f"A: {len(va)} voxels, B: {len(vb)} voxels, shared: {shared} "
      f"({shared / max(1, min(len(va), len(vb))) * 100:.1f}% of smaller view)")
