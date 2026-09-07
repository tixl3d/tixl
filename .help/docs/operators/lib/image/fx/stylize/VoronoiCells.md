# VoronoiCells

*in [Lib.image.fx.stylize](README.md)*

Creates a Voronoi cell pattern based on an incoming image.

For similar effects or interesting combinations see: [MosaicTiling] [VoronoiCells] [SubdivisionStretch] [HoneyCombTiles] [TriangleGridTransition] [Dither] [AsciiRender]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **EdgeColor** (Vector4) | Defines the color of the edges |
| **Background** (Vector4) | — |
| **Scale** (Single) | Defines the scaling of the effect / the amount of Voronoi cells visible |
| **EdgeWidth** (Single) | Defines the thickness of the edges |
| **Resolution** (Int2) | Defines the resolution of the output image |
| **Phase** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

