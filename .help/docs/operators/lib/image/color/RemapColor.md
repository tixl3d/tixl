# RemapColor

*in [Lib.image.color](README.md)*

Replaces the colors of an image with a gradient based on its brightness. This can be used to emulate gradient curves or level adjustments or color cycling effects.

Tip: This operator can be used to simulate "adjustment curves" from image manipulation software when gradient interpolation is set to spline.

Note: The gradient's alpha channel can control the intensity of the effect.

Also consider using [Tint].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **Gradient** (Gradient) | — |
| **Mode** (Int32) | — |
| **Exposure** (Single) | — |
| **GainAndBias** (Vector2) | — |
| **Cycle** (Single) | — |
| **WrapMode** (TextureAddressMode) | — |
| **DontColorAlpha** (Boolean) | — |
| **Resolution** (Int2) | — |
| **Repeat** (Single) | — |
| **GradientSteps** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

