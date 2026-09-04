# DirectionalBlur

*in [Lib.image.fx.blur](README.md)*

Blurs the incoming image along a directional angle.

You can override the angle and strength through an FXTexture with:
R - Angle
G - Strength

Using an effects texture, especially one that is a blurred version of the input image, is particularly interesting.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Size** (Single) | — |
| **Samples** (Single) | — |
| **Angle** (Single) | — |
| **FxTextures** (Texture2D) | — |
| **FxAngleFactor** (Single) | — |
| **FxSizeFactor** (Single) | — |
| **RefinementPass** (Boolean) | — |
| **RefinementSamples** (Int32) | — |
| **RefineSizeFactor** (Single) | — |
| **Wrap** (TextureAddressMode) | — |
| **Resolution** (Int2) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

