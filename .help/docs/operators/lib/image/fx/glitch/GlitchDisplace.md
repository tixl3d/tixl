# GlitchDisplace

*in [Lib.image.fx.glitch](README.md)*

Takes the incoming image and applies an image effect that glitches and displaces parts of the image to mimic lossy signals, broken video files, codec glitches etc.

Vaguely similar Op: [SortPixelGlitch]


Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAbberation] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Rows** (Int32) | Defines how many glitch bars are generated vertically |
| **Columns** (Int32) | Defines how many glitch bars are generated horizontally |
| **Size** (Single) | Scales the size of the glitch elements<br/>High value can make this effect take a lot of resources |
| **Amount** (Single) | Defines the overall intensity of the effect |
| **Threshold** (Single) | Defines based on the method which part of the image the effect is applied to |
| **Stretch** (Vector2) | Scales the glitching bars<br/>X = width<br/>Y = height |
| **Offset** (Vector2) | Randomly offsets the bars<br/>X = horizontally<br/>Y = vertically |
| **Scatter** (Vector2) | Randomly scatters the position of the glitch elements |
| **ScatterStretch** (Vector2) | Defines the maximum distance of the scattering |
| **ScatterOffset** (Vector2) | — |
| **Colorize** (Vector4) | Defines a color that is added to the elements |
| **ColorRatio** (Single) | Defines the color intensity |
| **Seed** (Int32) | Random seed for the glitch effect |
| **OverridePoints** (BufferWithViews) | — |
| **Mode** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Output2** | T3.Core.DataTypes.Texture2D |

