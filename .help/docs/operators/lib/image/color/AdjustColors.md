# AdjustColors

*in [Lib.image.color](README.md)*

Adjusts various color properties of the incoming image and adds a slight vignette.

Operator with more options that also allows objects far away from the camera to be colored differently than close objects: [ColorGradeDepth]

Also consider using [ColorGrade].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **Colorize** (Vector4) | — |
| **Saturation** (Single) | — |
| **Hue** (Single) | — |
| **Contrast** (Single) | — |
| **Exposure** (Single) | — |
| **Brightness** (Single) | — |
| **PreventClamping** (Vector2) | — |
| **Vignette** (Single) | — |
| **OrangeTeal** (Single) | — |
| **Background** (Vector4) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

