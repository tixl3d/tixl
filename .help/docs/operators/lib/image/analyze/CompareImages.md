# CompareImages

*in [Lib.image.analyze](README.md)*

A simple helper to verify an image effect before and after. It shows both images divided by a movable border which also acts as a comparison monitor.

Also see: [ImageLevels] [WaveForm]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | Input Image A |
| **Texture2d2** (Texture2D Required) | Input Image B |
| **Rotate** (Single) | Rotates the border |
| **Center** (Vector2) | Defines the center of the border |
| **IntensityRange** (Single) | Defines how large the border / monitor is displayed |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

