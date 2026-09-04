# DrawMeshCelShading

*in [Lib.mesh.draw](README.md)*

This is an attempt to create a cel shader. In fact, it's more a gradient-based shading as you need to use a gradient to define what colors will be used to shade your mesh. Make sure to use at least one [PointLight] and adjust the Brightness and the light intensity. You can set the gradient interpolation to hold, but you can still use the other interpolations; then you'll have full control, for example, if you don't want hard edges between colors.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Color** (Vector4) | — |
| **EdgeColor** (Vector4) | — |
| **Brightness** (Single) | — |
| **EdgeThresold** (Single) | — |
| **Shading** (Gradient) | — |
| **Culling** (CullMode) | — |
| **EnableZTest** (Boolean) | — |
| **EnableZWrite** (Boolean) | — |
| **ColorMap** (Texture2D) | — |
| **Mesh** (MeshBuffers Required) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

