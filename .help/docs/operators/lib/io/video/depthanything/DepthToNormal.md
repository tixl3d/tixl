# DepthToNormal

*in [Lib.io.video.depthanything](README.md)*

Converts a depth map into a normal map.

Uses Sobel gradients of the depth to derive surface normals.

AKA: normals from depth, depth normals

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **DepthTexture** (Texture2D) | The depth map to convert. |
| **Strength** (Single) | Scales the depth gradient, exaggerating or flattening the normals. |
| **Invert** (Boolean) | Flips the normal direction. |

## Outputs

| Name | Type |
|---|---|
| **NormalTexture** | T3.Core.DataTypes.Texture2D |


