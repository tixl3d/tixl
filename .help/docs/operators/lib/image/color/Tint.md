# Tint

*in [Lib.image.color](README.md)*

Tints the bright and dark colors of an image. This can be an easy method for subtle color corrections to achieve a wide variety of conversion effects, like grayscale or threshold.


Also, see [RemapColor] for a similar operator.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **Amount** (Single) | The strength of the effect. If fully applied the image will lose its original color information and only the brightness information will be used to blend between the two color parameters.<br/>Negative colors will reverse the effect. |
| **MapBlackTo** (Vector4) | Dark colors will become this color.<br/><br/>Tip: You can use the alpha channel to create a masking effect. |
| **MapWhiteTo** (Vector4) | Light colors will become this |
| **Exposure** (Single) | A factor that is applied before using the brightness. This can be useful to constrain HDR colors with levels above 1 into the mapped range. |
| **ChannelWeights** (Vector4) | A helper to apply different weights to the input image color channels. This can be useful for creating more natural grayscale conversions or to only use the alpha channel. |
| **GainAndBias** (Vector2) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

