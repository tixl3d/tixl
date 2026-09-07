# Layer2d

*in [Lib.render.basic](README.md)*

Creates a 2D plane in 3D space onto which the incoming image is rendered.
This op automatically adjusts to the correct aspect ratio.

A possible alternative [QuadMesh] -> [DrawMesh] -> [SetMaterial] in combination with [RenderTarget].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture** (Texture2D Required) | Texture input for any 2D op.<br/>e.g. [RenderTarget], [LoadImage] etc. |
| **Position** (Vector2) | The horizontal and vertical position of the layer in world space. <br/>With the default camera the viewport at that distance is precisely two units high. So setting y to 1 will align the lower layer border to the screen center. |
| **PositionZ** (Single) | Distance to the viewport.<br/>Positive values reduce the distance. Negative values increase it. |
| **Rotate** (Single) | Positive values turn the plane counterclockwise, negative values turn it clockwise. |
| **Stretch** (Vector2) | Non-uniform scaling of the plane.<br/>X = Width<br/>Y = Height |
| **Scale** (Single) | Uniformly scales the plane. |
| **ScaleMode** (Int32) | Selects the mode in which the plane should dynamically adapt to the aspect ratio of the viewport. |
| **Color** (Vector4) | Selects a color which will be multiplied with the incoming image. |
| **BlendMode** (Int32) | Selects the blend mode. |
| **EnableDepthTest** (Boolean) | This defines whether the mesh / plane covers itself or is covered by or covers other meshes. |
| **EnableDepthWrite** (Boolean) | This defines whether the mesh / plane covers itself or is covered by or covers other meshes. |
| **Filtering** (Filter) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

