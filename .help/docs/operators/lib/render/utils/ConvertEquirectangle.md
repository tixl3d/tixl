# ConvertEquirectangle

*in [Lib.render.utils](README.md)*

Transforms a 360° image that was captured using a mirror ball or a panorama camera via equirectangular mapping so that it can be projected onto a cube without being distorted.

Useful combinations: [LoadImage]

Also see: [Equirectangle], [PolarCoordinates]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **Resolution** (Int2) | — |

## Outputs
| Name | Type |
|---|---|
| **ColorBuffer** | T3.Core.DataTypes.Texture2D |

