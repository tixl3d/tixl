# AfterGlow2

*in [Lib.image.fx.feedback](README.md)*

Creates an afterglow effect for moving images

Other Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAberration] [Glow] [Grain] [Blur]

All Feedback Ops: [FluidFeedback] [AdvancedFeedback] [AdvancedFeedback2]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **DecayRate** (Single) | Defines how quickly the afterglow fades |
| **GlowImpact** (Single) | Controls the intensity of a glow effect |
| **BlurAmount** (Single) | Controls how much the images created by the afterglow effect are blurred |
| **Color** (Vector4) | Defines a color that is added to the afterglow |
| **OrgColor** (Vector4) | Defines a color that is added to the original image |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |
| **CombinedLayers** | T3.Core.DataTypes.Command |

