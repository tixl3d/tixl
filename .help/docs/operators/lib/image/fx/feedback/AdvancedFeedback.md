# AdvancedFeedback

*in [Lib.image.fx.feedback](README.md)*

An advanced version of the [FluidFeedback] effect is much more versatile but harder to control. It also utilizes [DetectEdges] and stabilizes the feedback buffer to a meaningful value range, thus avoiding black or overly bright values.


All Feedback Ops: [FluidFeedback] [AdvancedFeedback] [AdvancedFeedback2]

Also see [AfterGlow2]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command Required) | — |
| **Displacement** (Single) | — |
| **Shade** (Single) | — |
| **BlurRadius** (Single) | — |
| **SampleDistance** (Single) | — |
| **SampleRadius** (Single) | — |
| **Twist** (Single) | — |
| **Zoom** (Single) | — |
| **Rotate** (Single) | — |
| **Offset** (Vector2) | — |
| **DisplaceOffset** (Single) | — |
| **ShiftHue** (Single) | — |
| **ShiftSaturation** (Single) | — |
| **ShiftBrightness** (Single) | — |
| **LimitBrights** (Single) | — |
| **AmplifyEdges** (Single) | — |
| **Reset** (Boolean) | — |
| **IsEnabled** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **ColorBuffer** | T3.Core.DataTypes.Texture2D |

