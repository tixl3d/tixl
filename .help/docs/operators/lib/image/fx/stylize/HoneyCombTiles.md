# HoneyCombTiles

*in [Lib.image.fx.stylize](README.md)*

Creates a hexagonal pattern based on an incoming image.
Tip: Use [AdjustColors] on the incoming image to manipulate the pattern

For similar effects or interesting combinations see: [MosaicTiling] [VoronoiCells] [SubdivisionStretch] [HoneyCombTiles] [TriangleGridTransition] [Dither] [AsciiRender]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Fill** (Vector4) | — |
| **Background** (Vector4) | — |
| **Center** (Vector2) | Shifts the position of the Tiles without affecting the incoming image |
| **Rotation** (Single) | Rotates the pattern without rotating the incoming image |
| **Divisions** (Single) | Defines the size of the Tiles |
| **LineThickness** (Single) | Defines the thickness of the lines between the tiles |
| **MixOriginal** (Single) | Defines if and how much the incoming image is multiplied with the generated pattern |
| **Resolution** (Int2) | Overwrites the resolution |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

