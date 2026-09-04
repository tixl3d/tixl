# KeyColor

*in [Lib.image.color](README.md)*

A simple color keyer.
This can be used to select a color / range of colors in a 2D image / rendering / video to replace it with something else. For example, with a different color or turn it transparent.
This can be used for Bluescreen / Greenscreen footage.
Useful combinations: [LoadImage], [PlayVideo], [PlayVideoClip]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **Key** (Vector4) | — |
| **Exposure** (Single) | — |
| **WeightHue** (Single) | — |
| **WeightSaturation** (Single) | — |
| **WeightBrightness** (Single) | — |
| **Amplify** (Single) | — |
| **Choke** (Single) | — |
| **Return** (Int32) | — |
| **Background** (Vector4) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

