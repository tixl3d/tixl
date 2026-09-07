# SamplePointColorAttributes

*in [Lib.point.modify](README.md)*

Use a texture to color the points. Same as [SamplePointAttributes] but for colors only

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | — |
| **Texture** (Texture2D Required) | — |
| **BaseColor** (Vector4) | Use the alpha channel to control the blending of the texture |
| **BlendMode** (Int32) | — |
| **Center** (Vector3) | — |
| **Stretch** (Vector2) | — |
| **Scale** (Single) | — |
| **TextureRotate** (Vector3) | — |
| **TextureMode** (TextureAddressMode) | — |
| **Visibility** (GizmoVisibility) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

