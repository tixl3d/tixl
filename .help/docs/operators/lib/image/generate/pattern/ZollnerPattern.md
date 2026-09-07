# ZollnerPattern

*in [Lib.image.generate.pattern](README.md)*

Creates an image for an optical illusion.

Originally programmed for [WorksForEverybody]

Other interesting patterns can be generated with [SinForm] [ZollnerPattern] [FraserGrid] [Raster] [CheckerBoard] [RyojiPattern2] [RyojiPattern1]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **Fill** (Vector4) | Defines the color of the line |
| **Background** (Vector4) | Defines the background color |
| **Stretch** (Vector2) | Stretches the pattern on the x and y axis |
| **Offset** (Vector2) | Transforms the pattern on the x and y axis |
| **Scale** (Single) | Uniformly scales the pattern |
| **Rotate** (Single) | Rotates the pattern |
| **Feather** (Single) | Smoothes the edges |
| **BarWidth** (Single) | Defines the thickness of the center bars |
| **HookRotation** (Single) | Shifts every second space between the bars |
| **HookLength** (Single) | Defines the length of the hooks |
| **HookWidth** (Single) | Defines the thickness of the hooks |
| **RowSwift** (Single) | Shifts the picture elements with increasing intensity towards the edge |
| **RAffects_BarWidth** (Single) | — |
| **GAffects_HookLength** (Single) | — |
| **BAffects_HookRotation** (Single) | — |
| **Resolution** (Int2) | — |
| **AmplifyIllusion** (Single) | Rotates bars in pairs in opposite directions |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

