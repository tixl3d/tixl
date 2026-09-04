# Dither

*in [Lib.image.fx.stylize](README.md)*

Applies Floyd-Steinberg dithering to an image to convert it to black and white colors.

For similar effects or interesting combinations see: [MosaicTiling] [VoronoiCells] [SubdivisionStretch] [HoneyCombTiles] [TriangleGridTransition] [Dither] [AsciiRender]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **ShadowColor** (Vector4) | Color selection for all dark pixels. |
| **HighlightColor** (Vector4) | Color selection for all bright pixels. |
| **Method** (Int32) | Setting is currently ignored. |
| **GrayScaleWeights** (Vector4) | Defines which color channels are used to determine the light-dark distinction.<br/> |
| **GainAndBias** (Vector2) | — |
| **Scale** (Single) | Uniformly scales the size of the effect. |
| **Offset** (Vector2) | — |
| **BlendMethod** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

