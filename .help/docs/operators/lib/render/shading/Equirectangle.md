# Equirectangle

*in [Lib.render.shading](README.md)*

Renders the input scene as an image with depth with equirectangular mapping (for 360/VR/fulldome video)

Also see [PolarCoordinates], [ConvertEquirectangle]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputCommand** (Command) | — |
| **Dimension** (Int32) | Use 256, 512, 1024 or 2048 |

## Outputs
| Name | Type |
|---|---|
| **OutputColor** | T3.Core.DataTypes.Texture2D |
| **OutputDepth** | T3.Core.DataTypes.Texture2D |

