# MirrorRepeat

*in [Lib.image.transform](README.md)*

Shifts, slices or mirrors the incoming image to create endless textures or kaleidoscopic patterns when combined with itself

A more sophisticated version: [KochKaleidoskope]
For simpler transformations: [TransformImage]

Similar effects or interesting combinations: [MosaicTiling] [VoronoiCells] [SubdivisionStretch]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | Image input |
| **RotateMirror** (Single) | Rotates the result |
| **RotateImage** (Single) | Rotates the incoming image before it is mirrored |
| **Width** (Single) | Defines the distance between the mirrors. A smaller distance means many mirrored stripes. |
| **Offset** (Single) | Offsets the result along the mirror axis |
| **OffsetEdge** (Single) | — |
| **Offsetimage** (Vector2) | Offsets the image before it is mirrored |
| **ShadeAmount** (Single) | Darkens one side of the mirrored image<br/><br/>Hint: If combined with a small width it creates a 'Fanfold' effect |
| **ShadeColor** (Vector4) | Defines the color that is used for shading |
| **Resolution** (Int2) | Defines the resolution of the result in pixels |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

