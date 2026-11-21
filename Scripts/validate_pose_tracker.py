"""Validate the depth-ego-motion tracker design that will be ported into
DepthAnything (grid-NN ICP, trimmed inliers, damped similarity scale,
constant-velocity seeding, re-anchor on failure).

Run: python Scripts/validate_pose_tracker.py <model.onnx>
"""
import sys
import numpy as np
import onnxruntime as ort

rng = np.random.default_rng(11)
W = H = 504
FX = 700.0
MODEL = sys.argv[1]

def rot(axis, deg):
    a = np.radians(deg); c, s = np.cos(a), np.sin(a)
    return (np.array([[c,0,s],[0,1,0],[-s,0,c]]) if axis=="y" else np.array([[1,0,0],[0,c,-s],[0,s,c]]))

# rich GT scene: far wall + three boxes at different depths (perspective structure)
Z_FAR = 3.0
tex_far = rng.integers(0, 255, (30, 45, 3), np.uint8).repeat(16,0).repeat(16,1)[:504,:504]
boxes = [(0.10, -0.15, 1.0, 0.12), (-0.25, 0.18, 1.6, 0.15), (0.30, 0.25, 2.2, 0.18)]
box_tex = [rng.integers(0, 255, (20, 20, 3), np.uint8).repeat(16,0).repeat(16,1) for _ in boxes]

def render(R, t):
    yy, xx = np.mgrid[0:H, 0:W]
    d_cam = np.stack([(xx-W/2)/FX, (yy-H/2)/FX, np.ones_like(xx,float)], -1).reshape(-1,3)
    d_w = d_cam @ R
    o = -(R.T @ t)
    out = np.zeros((H*W,3), np.uint8)
    s = (Z_FAR - o[2]) / d_w[:, 2]
    pt = o + s[:,None]*d_w
    ins = (s>0) & (np.abs(pt[:,0])<=3) & (np.abs(pt[:,1])<=3)
    u = (pt[ins,0]/6+0.5)*(tex_far.shape[1]-1); v=(pt[ins,1]/6+0.5)*(tex_far.shape[0]-1)
    ui=np.round(u).astype(int); vi=np.round(v).astype(int)
    out[ins] = tex_far[vi, ui]
    img = out.reshape(H,W,3); depth_s = np.where(ins, s, 1e9)
    for (bx,by,bz,half), bt in zip(boxes, box_tex):
        s2 = (bz - o[2]) / d_w[:, 2]
        p2 = o + s2[:,None]*d_w
        ins2 = (s2>0) & (np.abs(p2[:,0]-bx)<=half) & (np.abs(p2[:,1]-by)<=half)
        if ins2.sum() == 0: continue
        u2=((p2[ins2,0]-bx+half)/(2*half)*(bt.shape[1]-1)).astype(int).clip(0,bt.shape[1]-1)
        v2=((p2[ins2,1]-by+half)/(2*half)*(bt.shape[0]-1)).astype(int).clip(0,bt.shape[0]-1)
        out2 = out.copy(); out2[ins2] = bt[v2, u2]
        out = out2
        depth_s = np.where(ins2 & (s2 < depth_s), s2, depth_s)
    return out.reshape(H,W,3)

sess = ort.InferenceSession(MODEL, providers=["CPUExecutionProvider"])
name = sess.get_inputs()[0].name
outs = [o.name for o in sess.get_outputs()]
def prep(img):
    x = (img.astype(np.float32)/255.0 - np.array([0.485,0.456,0.406],np.float32))/np.array([0.229,0.224,0.225],np.float32)
    return np.ascontiguousarray(x.transpose(2,0,1)[None].astype(np.float32))
def infer(img):
    by = dict(zip(outs, sess.run(None, {name: prep(img)})))
    return by["depth"].reshape(H,W), by["depth_conf"].reshape(H,W), by["intrinsics"].reshape(3,3)

# ---------------- tracker (the design that gets ported to C#) ----------------
class Tracker:
    CELL = 0.06
    def __init__(self):
        self.prev = None            # (N,3) points in prev camera units
        self.world = np.eye(4)      # camToWorld accumulated
        self.last_delta = (np.eye(3), np.zeros(3), 1.0)
        self.scale = 1.0

    def cloud(self, depth, conf, K):
        u, v = np.meshgrid(np.arange(0, W, 4), np.arange(0, H, 4))
        d = depth[::4, ::4]; c = conf[::4, ::4]
        m = np.isfinite(d) & (c >= np.median(c))
        z = d[m]
        x = (u[m] - K[0,2])/K[0,0]*z; y = (v[m] - K[1,2])/K[1,1]*z
        return np.stack([x, y, z], -1)

    def grid(self, pts):
        g = {}
        cells = np.floor(pts / self.CELL).astype(np.int64)
        keys = (cells[:,0]*73856093) ^ (cells[:,1]*19349663) ^ (cells[:,2]*83492791)
        for i, k in enumerate(keys):
            g.setdefault(int(k), []).append(i)
        return g, cells

    def match(self, grid, cells, pts):
        out_j = np.full(len(pts), -1); out_d = np.full(len(pts), 1e9)
        for i, p in enumerate(pts):
            c = cells[i]; best = 1e9; bj = -1
            for dx in (-1,0,1):
                for dy in (-1,0,1):
                    for dz in (-1,0,1):
                        k = int((c[0]+dx)*73856093) ^ int((c[1]+dy)*19349663) ^ int((c[2]+dz)*83492791)
                        for j in grid.get(k, ()):
                            dd = float(((pts[i]-self.prev[j])**2).sum())
                            if dd < best: best, bj = dd, j
            out_j[i], out_d[i] = bj, best
        return out_j, out_d

    def umeyama(self, src, dst, s_prev):
        cs, cd = src.mean(0), dst.mean(0)
        cov = (dst - cd).T @ (src - cs)
        U, S, Vt = np.linalg.svd(cov)
        D = np.eye(3); D[2,2] = np.sign(np.linalg.det(U @ Vt))
        R = U @ D @ Vt
        s = S.sum() / max(1e-12, ((src - cs)**2).sum())
        s = float(np.clip(s, s_prev*0.95, s_prev*1.05))
        return R, cd - R @ (s*cs), s

    def update(self, depth, conf, K):
        cur = self.cloud(depth, conf, K)
        if self.prev is None:
            self.prev = cur
            return np.eye(4), True
        R, t, s = self.last_delta
        src = cur.copy()  # we find cur->prev delta, then invert
        est = (np.eye(3), np.zeros(3), 1.0)
        ok = False
        for it in range(10):
            moved = (s*(R @ src.T)).T + t
            g, cells = self.grid(self.prev)
            mc = np.floor(moved / self.CELL).astype(np.int64)
            j, d2 = self.match(g, mc, moved)
            good = j >= 0
            if good.sum() < len(moved)*0.25: break
            thr = np.partition(d2[good], int(good.sum()*0.7))[int(good.sum()*0.7)]
            k = good & (d2 <= thr)
            dR, dt_, ds = self.umeyama(src[k], self.prev[j[k]], s)
            R, t, s = dR @ R, dR @ (s*t) + dt_, ds
            est = (R, t, s); ok = True
        extent = float(np.percentile(self.prev[:,2], 90) - np.percentile(self.prev[:,2], 10))
        # accept/anchor decision below re-matches once for stats
        # residual check (simplified): re-match once for stats
        moved = (s*(R @ src.T)).T + t
        g, cells = self.grid(self.prev)
        mc = np.floor(moved / self.CELL).astype(np.int64)
        j, d2 = self.match(g, mc, moved)
        good = j >= 0
        med = float(np.median(np.sqrt(d2[good]))) if good.sum() > 50 else 1e9
        accept = good.sum() >= len(moved)*0.3 and med < self.CELL
        if not accept:
            self.prev = cur
            self.last_delta = (np.eye(3), np.zeros(3), 1.0)
            return np.eye(4), False   # re-anchor
        # camToWorld_k = camToWorld_{k-1}  o  inv(delta),  delta maps cur-units -> prev-units
        delta = np.eye(4); delta[:3,:3] = s * R; delta[:3,3] = t
        self.world = self.world @ delta
        self.last_delta = (R, t, s)
        self.prev = cur
        return self.world.copy(), True

def run_sequence(label, motions):
    frames = []
    R_w = np.eye(3); t_w = np.zeros(3)
    tr = Tracker()
    pos_err = []; rot_err = []
    gt_world = np.eye(4)
    for i, (ryaw, rpitch, tx, ty) in enumerate(motions):
        R_w = rot("y", ryaw) @ rot("x", rpitch) @ R_w
        t_w = t_w + R_w @ np.array([tx, ty, 0.0])
        img = render(R_w, t_w)
        depth, conf, K = infer(img)
        world, accepted = tr.update(depth, conf, K)
        gt_world = np.eye(4); gt_world[:3,:3] = R_w; gt_world[:3,3] = t_w
        p_est = world[:3,3]; p_gt = gt_world[:3,3]
        R_err = np.degrees(np.arccos(np.clip((np.trace(world[:3,:3].T @ R_w)-1)/2, -1, 1)))
        pos_err.append(np.linalg.norm(p_est - p_gt))
        rot_err.append(R_err)
    print(f"{label}: frames={len(motions)}  final pos err {pos_err[-1]:.4f}  final rot err {rot_err[-1]:.2f} deg  "
          f"(path {[f'{e:.3f}' for e in pos_err]})")

run_sequence("S1 orbit (5f, 6deg+3deg yaw/pitch +0.04 lateral per frame)",
             [(6,3,0.04,0.015)]*5)
run_sequence("S2 pure translation (4f)", [(0,0,0.06,0.02)]*4)
run_sequence("S3 pure rotation (4f, 8deg yaw)", [(8,0,0.0,0.0)]*4)
