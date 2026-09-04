# BlendImages

*in [Lib.image.use](README.md)*

Blends the connected input images with cross-fading and using a float index.

Please note: this will lead to inconsistent behavior if used with images of varying resolution unless you set an explicit resolution.

Also see: [PickTexture]


All blend ops and similar: [BlendImages] [Blend] [BlendWithMask] [Combine3Images]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **BlendFraction** (Single) | — |
| **Input** (Texture2D Required) | — |
| **Resolution** (Int2) | — |

## Outputs
| Name | Type |
|---|---|
| **OutputImage** | T3.Core.DataTypes.Texture2D |

