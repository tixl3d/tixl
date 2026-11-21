# DepthParallax

*in [Lib.io.video.depthanything](README.md)*

Shifts an image by its depth, as if the camera moved sideways.

Gives a still image or live video a 3D feel from a depth map: nearer surfaces move further than distant ones when Offset changes. Each pixel searches for the nearest surface that lands on it, so no holes open up, at the price of some stretching at depth edges.

Tips:
- Animate Offset with a slow circle or an LFO for the classic 3D-photo wobble.
- Wire MinDepth and MaxDepth from DepthAnything so the effect stays steady as the scene changes.

AKA: 2.5d, 3d photo, depth displacement, parallax, depth warp

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **FocalPlane** (Single) | Where the depth stays put, from 0 (farthest) to 1 (nearest). Nearer surfaces move one way, farther ones the other. |
| **Steps** (Int32) | Search steps per pixel. More steps follow sharp depth edges better and cost more. |
| **ImageTexture** (Texture2D) | The image to shift. |
| **DepthTexture** (Texture2D) | The depth map belonging to the image. |
| **MinDepth** (Single) | Depth value at one end of the range. Wire DepthAnything's MinDepth here. |
| **MaxDepth** (Single) | Depth value at the other end of the range. Wire DepthAnything's MaxDepth here. |
| **Offset** (Vector2) | Size and direction of the virtual camera move, as a fraction of the image. The nearest surface moves by this much. |

## Outputs

| Name | Type |
|---|---|
| **ResultTexture** | T3.Core.DataTypes.Texture2D |


