# ConvertFormat

*in [Lib.image.color](README.md)*

Converts a texture into another format. This can be useful to speed up video file rendering or to fix unsupported texture formats for [PlayVideo].

For fast video exporting use [B8G8R8A8_UNorm].

Please note: The ScaleImage is only experimental and will crop from the upper left corner.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **Format** (Format) | — |
| **GenerateMipMaps** (Boolean) | — |
| **ScaleFactor** (Single) | — |
| **Enable** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

