# DepthToPoints

*in [Lib.io.video.depthanything](README.md)*

Unprojects a depth map into a camera-space point buffer.

One point per pixel (or per PixelStride-th pixel) is placed at the depth, colored from the color texture, with its Scale set to the world-space size that covers SplatSizePixels screen pixels. Connect the buffer to DrawPoints with UsePointsScale enabled: DrawPoints billboards each point in camera space, so the result is a live depth splat that can be orbited and moved like a 3D object.

Invalid depths and points below ConfidenceThreshold are written as zero-scale transparent points, so no compaction pass is needed.

AKA: depth splat, point cloud, live splat, unproject, depth to 3D

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **DepthTexture** (Texture2D) | DepthAnything's DepthTexture (R32_Float). V3 outputs depth, V2 outputs disparity. |
| **PixelStride** (Int32) | Sample every Nth pixel. 2 quarters the point count; raise it for dense inputs or slower GPUs. |
| **Opacity** (Single) | Alpha of each point. Lower values blend overlapping points more softly. |
| **DepthScale** (Single) | Scales the distance of every point from the camera. Relative depth has no unit, so this sets the size of the reconstruction. |
| **FieldOfView** (Single) | Vertical field of view in degrees. Wire DepthAnything's VerticalFov for the model's estimate. |
| **Enabled** (Boolean) | Toggle the point generation. |
| **MaxPoints** (Int32) | Upper bound for the buffer. The stride is grown automatically when PixelStride would exceed it. |
| **SplatSizePixels** (Single) | Screen-space diameter of a point's splat, in pixels, at its depth. Point size in DrawPoints multiplies this. |
| **ConfidenceThreshold** (Single) | Minimum confidence for a point to be kept. Only used when ConfidenceTexture is connected. |
| **ColorTexture** (Texture2D) | Color sampled for each point. When unconnected, points are white. |
| **TemporalSmoothing** (Single) | Blends each point with the previous frame's buffer. 0 is raw and most responsive; 0.8-0.9 calms a still webcam, while lower values reduce lag on a moving camera. |
| **ConfidenceTexture** (Texture2D) | Optional confidence map; points below ConfidenceThreshold are dropped. |

## Outputs

| Name | Type |
|---|---|
| **Points** | T3.Core.DataTypes.BufferWithViews |
| **PointCount** | System.Int32 |


