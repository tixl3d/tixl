# SinForm

*in [Lib.image.generate.pattern](README.md)*

Generates a sine curve with various parameters

The presets show various application possibilities for different patterns


Other interesting patterns can be generated with [SinForm] [ZollnerPattern] [FraserGrid] [Raster] [CheckerBoard] [RyojiPattern2] [RyojiPattern1]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | Custom input for a background image |
| **Fill** (Vector4) | Defines the color of the line |
| **Background** (Vector4) | Defines the background color |
| **LineWidth** (Single) | Defines the thickness of the lines |
| **Fade** (Single) | Defines the smoothness of the line edges |
| **Size** (Vector2) | Scales the lines on the x and y axis |
| **Offset** (Vector2) | Transforms the position of the line |
| **Rotate** (Single) | Rotates the line |
| **Copies** (Single) | Defines the amount of duplicated lines |
| **OffsetCopies** (Vector2) | Offsets the duplicated lines |
| **Resolution** (Int2) | — |
| **TextureFormat** (Format) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

