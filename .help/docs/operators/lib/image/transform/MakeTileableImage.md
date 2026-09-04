# MakeTileableImage

*in [Lib.image.transform](README.md)*

Makes an incoming image tileable based on linear edge Falloff.

This can be used to repeat an imported or generated texture infinitely without any obviously visible borders.

Also see [MakeTileableImageAdvanced] for a version with more options.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ImageA** (Texture2D) | — |
| **EdgeFallOff** (Single) | Defines the length / size of the transition |
| **TilingMode** (Int32) | Defines how the image is tiled:<br/><br/>0 = No Tiling<br/>1 = horizontal tiling only<br/>2 = vertical tiling only<br/>3 = Vertical and horizontal tiling |
| **IsEnabled** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Selected** | T3.Core.DataTypes.Texture2D |

