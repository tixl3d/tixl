# SubdivisionStretch

*in [Lib.image.fx.glitch](README.md)*

A powerful image generation effect that splits an image into smaller and smaller fragments. The splits occur horizontal or vertical within the given split range.

This effect is especially nice, because it can be animated either with the RandomPhase parameter or with the ScrollOffset. Combining it with an incoming image is also great, because it can produce great glitch effects.

It's similar to [RyojiPattern] and [RyojiPattern2] but produces more random results. Check out the presets and parameter documentation.


For similar effects or interesting combinations see: [MosaicTiling] [VoronoiCells] [SubdivisionStretch] [HoneyCombTiles] [TriangleGridTransition] [Dither] [AsciiRender]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | A source image from which each cell samples its color. |
| **MaxSubdivisions** (Int32) | The maximum number of splits applied to the image. |
| **Threshold** (Single) | The chance that the cell is no longer subdivided. If 0, every cell will be subdivided until MaxSubdivisions is reached. |
| **UseAspectForSplit** (Boolean) | If enabled, split direction will select the longer side. This will prevent cells from becoming too stretched. |
| **SplitCenter** (Single) | The position where a cell is split (0.5 is center).<br/> |
| **SplitCenterVariation** (Single) | A random amount added to the split position. This will be animated with the RandomPhase parameter. |
| **DirectionBias** (Single) | Skews the chance of the split direction to vertical (negative) or horizontal (positive) splits. |
| **ScrollOffset** (Vector2) | Add a scroll offset to every cell. This can be nicely combined with [AudioReaction] in AccumulativeSum mode. |
| **ScrollGainAndBias** (Vector2) | Controls the distribution of scroll speeds of each cell. |
| **RandomPhase** (Single) | Nicely animates the split position within the given SplitCenterVariation range. |
| **RandomSeed** (Int32) | An additional random seed. |
| **GapWidth** (Single) | Inserts a gap between cells. |
| **Feather** (Single) | Controls the sharpness of the gap. Negative numbers will invert the gap direction. |
| **GapColor** (Vector4) | Color of the gap. |
| **GradientMode** (Int32) | Controls how the colors are picked from the gradient: <br/><br/>Steps: use the cells subdivision count<br/>Random: Pick a random color for each cell. |
| **Gradient** (Gradient) | Provides an additional color palette. These colors are then multiplied onto the image color.<br/><br/>Try using this with RandomColor mode and hold interpolation of the gradient to create Piet Mondrian-like designs. |
| **ColorMode** (Int32) | — |
| **Use4xMSAA** (Boolean) | Apply a rotated grid super sampling (RGSS) to avoid flickering of edges. This requires the effect to be rendered 4 times and has a significant performance overhead. Only enable if flickering is a problem. |
| **TextureFx** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

