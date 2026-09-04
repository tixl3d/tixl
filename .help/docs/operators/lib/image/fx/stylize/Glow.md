# Glow

*in [Lib.image.fx.stylize](README.md)*

Adds a glow effect to the incoming image. It uses multiple resolution passes and is reasonably fast.
You can use the Offset parameter as a threshold for 'glowing' content.

Better alternative: [Bloom]


Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAbberation] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture** (Texture2D Required) | — |
| **Radius** (Single) | Controls how much the glow effect is blurred.<br/><br/>When heavily blurred, the effect consequently loses intensity. |
| **Intensity** (Single) | Controls the intensity of the bloom effect. |
| **AmplifyCore** (Single) | Further controls the intensity of the glow effect. |
| **Threshold** (Single) | Controls the brightness of the image and helps to control which areas of the image are included in the effect. |
| **Color** (Vector4) | Colorizes the Bloom effect. |
| **Samples** (Single) | Controls the fidelity of the effect.<br/>Higher numbers create smoother results at the cost of rendering speed. |
| **BlendMode** (Int32) | Selects how the effect is blended into the image. |

## Outputs
| Name | Type |
|---|---|
| **ImgOutput** | T3.Core.DataTypes.Texture2D |
| **Output** | T3.Core.DataTypes.Command |

