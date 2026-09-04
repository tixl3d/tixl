# TimeDisplace

*in [Lib.image.fx.distort](README.md)*

Uses a texture array history buffer to displace in time.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **DisplaceMap** (Texture2D Required) | — |
| **DisplaceMode** (Int32) | — |
| **Displacement** (Single) | — |
| **DisplacementOffset** (Single) | — |
| **Twist** (Single) | — |
| **Shade** (Single) | Darkens the effect |
| **SampleRadius** (Single) | — |
| **DisplaceMapOffset** (Vector2) | — |
| **WrapMode** (TextureAddressMode) | Defines if and how the image is repeated at its edge |
| **GenerateMips** (Boolean) | — |
| **RGSS_4xAA** (Boolean) | — |
| **TextureFiltering** (Filter) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

