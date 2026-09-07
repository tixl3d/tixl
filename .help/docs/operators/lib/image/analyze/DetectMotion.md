# DetectMotion

*in [Lib.image.analyze](README.md)*

Uses motion history or difference to detect changes in an image.
This might be useful for tracking.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **VideoTexture** (Texture2D Required) | — |
| **VideoFrameIndex** (Int32 Relevant) | For video inputs with frames different that 60fps (or the current refresh rate) you should pass in the frame count here.<br/><br/>This will prevent flickering. |
| **Method** (Int32) | — |
| **BackgroundFade** (Single) | — |
| **RemapColor** (Boolean) | Enable color remap using the gradient below. |
| **RemapGradient** (Gradient) | If enabled, can be used to increase contrast and reduce noise or key colors. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

