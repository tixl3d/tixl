# ChromaticDistortion

*in [Lib.image.fx.distort](README.md)*

Simulates an imaging error of optical camera lenses that manifests itself as blurring or discoloration at the outer edges.

Also see [ChromaticAberration]

Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAberration] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **Center** (Vector2) | Offsets the center of the effect. |
| **Size** (Single) | Controls the intensity of the effect. |
| **Colorize** (Single) | Tints the image yellow. |
| **Distort** (Single) | Controls intensity of lens distortion.<br/> |
| **DistortOffset** (Single) | Uniformly scales the image to fix unwanted effects at the edges when using "distort" setting. |
| **ScaleImage** (Single) | Uniformly scales the image without cutting the edges. |
| **SampleCount** (Int32) | Controls the fidelity of the effect.<br/>Higher numbers create smoother results at the cost of rendering speed. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

