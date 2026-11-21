#!/usr/bin/env python3
"""Export Depth Anything 3 Base as a fixed N-view ONNX model that predicts depth and camera poses jointly.

The community single-view exports have no usable camera branch: with one view the pose is the
reference identity. This exports the official model with a fixed pair of views, so the operator can
feed the previous and the current frame and read the current frame's pose relative to the previous one.

  input   images      float32 [1, N, 3, S, S]   ImageNet-normalized, view 0 = reference
  output  depth       [1, N, S, S]
          depth_conf  [1, N, S, S]
          extrinsics  [1, N, 3, 4]   view 0 is the reference (identity)
          intrinsics  [1, N, 3, 3]

N views are inferred together, so their depth and poses share one scale: a chunk of frames is consistent inside.
Chain chunks with overlapping frames (DA3-Streaming does this with a similarity transform per chunk). Defaults
(2 views, 504) reproduce da3-base-mv2.onnx; for scanning use e.g. --views 6 --size 392.

Setup (a venv keeps the official package's pinned dependencies away from your Python):
  python -m venv --system-site-packages venv
  venv/Scripts/python -m pip install --ignore-requires-python git+https://github.com/ByteDance-Seed/Depth-Anything-3
  venv/Scripts/python -m pip install addict
  venv/Scripts/python export_da3_multiview.py --out da3-base-mv2.onnx

--ignore-requires-python is needed because the package declares "<=3.13" and rejects 3.13.x patch releases.
Copy the result next to the other models as da3-base-mv2.onnx; the operator lists it as "V3 Base (camera tracking)".

The exporter needs four rewrites of the official package that ONNX cannot trace; they are applied to the
installed copy by text replacement (idempotent) before exporting.
"""
import argparse
import importlib.util
from pathlib import Path

REPO = "depth-anything/DA3-BASE"
SIZE = 504

# (file relative to the package, original text, replacement)
PATCHES = [
    ("model/dinov2/layers/rope.py",
     "positions = torch.cartesian_prod(y_coords, x_coords)",
     'yy, xx = torch.meshgrid(y_coords, x_coords, indexing="ij")\n'
     "            positions = torch.stack([yy.reshape(-1), xx.reshape(-1)], dim=-1)"),
    ("model/dinov2/vision_transformer.py",
     "x[:, :, 0] = cam_token",
     "x = torch.cat([cam_token[:, :, None], x[:, :, 1:]], dim=2)"),
    ("utils/geometry.py",
     "torch.cat([R.mT, -R.mT @ T], dim=-1)",
     "torch.cat([R.transpose(-1, -2), -R.transpose(-1, -2) @ T], dim=-1)"),
]


def patch_package():
    spec = importlib.util.find_spec("depth_anything_3")
    if spec is None:
        raise SystemExit("depth_anything_3 is not installed; see the setup notes at the top of this file")
    # The package ships without an __init__.py, so there is no origin file, only a search location
    root = Path(spec.origin).parent if spec.origin else Path(list(spec.submodule_search_locations)[0])
    for relative, original, replacement in PATCHES:
        path = root / relative
        text = path.read_text(encoding="utf-8")
        if replacement in text:
            continue
        if original not in text:
            raise SystemExit(f"{relative}: expected text not found; the package version differs from the one this script was written for")
        path.write_text(text.replace(original, replacement, 1), encoding="utf-8")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=None, help="Output file, default da3-base-mv<views>[-<size>].onnx")
    ap.add_argument("--views", type=int, default=2, help="Frames inferred together")
    ap.add_argument("--size", type=int, default=SIZE, help="Square input size, a multiple of 14")
    ap.add_argument("--repo", default=REPO, help="Official checkpoint to export")
    args = ap.parse_args()
    if args.size % 14 != 0 or args.views < 2:
        raise SystemExit("--size must be a multiple of 14 and --views at least 2")
    if args.out is None:
        args.out = f"da3-base-mv{args.views}" + ("" if args.size == SIZE else f"-{args.size}") + ".onnx"

    patch_package()

    import torch
    import depth_anything_3.model.da3 as da3
    from depth_anything_3.api import DepthAnything3
    from depth_anything_3.model.utils.transform import quat_to_mat

    def pose_encoding_to_extri_intri(pose_encoding, image_size_hw=None):
        # The original writes the intrinsics in place, which ONNX cannot express
        t = pose_encoding[..., :3]
        quat = pose_encoding[..., 3:7]
        fov_h = pose_encoding[..., 7]
        fov_w = pose_encoding[..., 8]
        extrinsics = torch.cat([quat_to_mat(quat), t[..., None]], dim=-1)
        h, w = image_size_hw
        fy = (h / 2.0) / torch.clamp(torch.tan(fov_h / 2.0), 1e-6)
        fx = (w / 2.0) / torch.clamp(torch.tan(fov_w / 2.0), 1e-6)
        zero = torch.zeros_like(fx)
        one = torch.ones_like(fx)
        cx = torch.full_like(fx, w / 2)
        cy = torch.full_like(fx, h / 2)
        intrinsics = torch.stack([torch.stack([fx, zero, cx], -1),
                                  torch.stack([zero, fy, cy], -1),
                                  torch.stack([zero, zero, one], -1)], -2)
        return extrinsics, intrinsics

    da3.pose_encoding_to_extri_intri = pose_encoding_to_extri_intri

    # Three or more views would pick a reference view from the data (a branch ONNX cannot trace); view 0 is the reference
    import depth_anything_3.model.dinov2.vision_transformer as vit
    vit.THRESH_FOR_REF_SELECTION = 10 ** 9

    net = DepthAnything3.from_pretrained(args.repo).model.eval()
    for p in net.parameters():
        p.requires_grad_(False)

    class Pair(torch.nn.Module):
        def __init__(self, model):
            super().__init__()
            self.model = model

        def forward(self, images):
            out = self.model(images)
            return out.depth, out.depth_conf, out.extrinsics, out.intrinsics

    images = torch.zeros(1, args.views, 3, args.size, args.size)
    torch.onnx.export(Pair(net), (images,), args.out,
                      input_names=["images"],
                      output_names=["depth", "depth_conf", "extrinsics", "intrinsics"],
                      opset_version=18, dynamo=False)
    print(f"wrote {args.out}")


if __name__ == "__main__":
    main()
