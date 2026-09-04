# AfterGlow

*in [Lib.image.fx.feedback](README.md)*

Creates an afterglow effect for moving images just like the newer version [AfterGlow2]

Other Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAberration] [Glow] [Grain] [Blur]

All Feedback Ops: [FluidFeedback] [AdvancedFeedback] [AdvancedFeedback2]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **BlurAmount** (Single) | — |
| **GlowImpact** (Single) | — |
| **DecayRate** (Single) | — |
| **ContrastOffset2** (Single) | — |
| **Resolution** (Int2) | — |
| **Color** (Vector4) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |
| **CombinedLayers** | T3.Core.DataTypes.Command |

