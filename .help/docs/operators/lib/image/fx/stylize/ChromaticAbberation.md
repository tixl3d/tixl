# ChromaticAbberation

*in [Lib.image.fx.stylize](README.md)*

Simulates an imaging error of optical camera lenses that manifests itself as blurring or discoloration at the outer edges.

Also see [ChromaticDistortion]

Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAberration] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Size** (Single) | Controls how far the individual colors are shifted away from each other at the edges of the image. |
| **Strength** (Single) | Controls the intensity of discoloration at the edges of the image. |
| **SampleCount** (Int32) | Controls the fidelity of the effect.<br/>Higher numbers create smoother results at the cost of rendering speed. |
| **Distort** (Single) | Simulates lens distortion.<br/>Negative values stretch the edges of the image outwards. Positive values pull them inwards. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

