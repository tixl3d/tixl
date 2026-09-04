# DrawScreenQuadAdvanced

*in [Lib.render.basic](README.md)*

Just like DrawScreecQuad but with Depth and Normal buffer support for special FX.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture** (Texture2D Relevant) | — |
| **Color** (Vector4) | Selects a color which will be multiplied with the incoming image. |
| **Width** (Single) | Multiplier for Width |
| **Height** (Single) | Multiplier for Height |
| **BlendMode** (Int32) | Selects the Blend mode |
| **EnableDepthTest** (Boolean) | This defines whether the Quad covers itself or is covered by or covers other meshes. |
| **EnableDepthWrite** (Boolean) | This defines whether the Quad covers itself or is covered by or covers other meshes. |
| **Position** (Vector2) | Manipulates the position of the Quad<br/>X = Right / Left<br/>Y = Up / Down |
| **Filter** (Filter) | Selects which Filter is used for rendering the incoming texture |
| **DepthBuffer** (Texture2D) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

