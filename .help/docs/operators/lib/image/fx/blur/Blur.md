# Blur

*in [Lib.image.fx.blur](README.md)*

Blurs the incoming image

Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAbberation] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Size** (Single) | Defines the strength/radius of the blur |
| **Samples** (Single) | Defines the number of iterations with which the image is blurred. <br/><br/>Higher values produce higher quality at the cost of computing speed. |
| **Offset** (Single) | Offsets the brightness of the image |
| **Opacity** (Single) | Changes the opacity/gamma of the image |
| **Resolution** (Int2) | Overwrites/redefines the resolution of the incoming image |
| **Wrap** (TextureAddressMode) | Defines what is displayed at the edge of the image or how it is repeated should it be cut off/offset over its borders. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

