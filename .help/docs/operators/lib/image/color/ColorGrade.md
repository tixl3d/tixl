# ColorGrade

*in [Lib.image.color](README.md)*

Adjusts the color grading of the incoming image for the Gain (highlights), Gamma (mid-tones), and Lift (shadows).

Gray values are neutral. Alpha can be used to amplify the effect. Vignette color, size, and position can be used for additional effects.

Try:
- blending by hovering over presets while holding the ALT key.
- dragging the parameter color swatches vertically with the left and right mouse buttons to quickly adjust brightness and alpha.
- adding a [WaveForm2] operator to visualize the image properties.

Operator with more options that also allows objects far away from the camera to be colored differently than close objects: [ColorGradeDepth]

Also consider using [AdjustColors].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **PreSaturate** (Single) | — |
| **Gain** (Vector4) | — |
| **Gamma** (Vector4) | — |
| **Lift** (Vector4) | — |
| **VignetteColor** (Vector4) | — |
| **VignetteRadius** (Single) | — |
| **VignetteFeather** (Single) | — |
| **VignetteCenter** (Vector2) | — |
| **GenerateMipmaps** (Boolean) | — |
| **ClampResult** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

