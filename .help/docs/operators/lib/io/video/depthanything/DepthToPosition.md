# DepthToPosition

*in [Lib.io.video.depthanything](README.md)*

Unprojects a depth map into camera-space positions.

Writes x, y and z of every pixel into the red, green and blue channels of a float texture, with x to the right, y up and z the distance in front of the camera. Alpha is 1 where the depth was usable and 0 elsewhere. The positions can drive displacement, particles or relighting with ordinary texture-reading operators.

AKA: depth to world, point cloud, unproject, depth to xyz

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **DepthTexture** (Texture2D) | The depth map to unproject. |
| **FieldOfView** (Single) | Vertical field of view of the camera in degrees. DepthAnything's VerticalFov gives the model's estimate. |
| **DepthScale** (Single) | Scales the distance of every point from the camera. Relative depth has no unit, so this sets the size of the reconstruction. |

## Outputs

| Name | Type |
|---|---|
| **PositionTexture** | T3.Core.DataTypes.Texture2D |


