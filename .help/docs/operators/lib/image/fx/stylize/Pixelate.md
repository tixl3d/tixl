# Pixelate

*in [Lib.image.fx.stylize](README.md)*

TilesAmount parameter works only if Divisor = 0. If Divisor is greater than 0, it will divide the resolution of your image and try to keep the tiles close to a square shape.

[MosaicTiling] is similar. I needed a "pixel perfect" alternative that allows defining the desired X and Y resolution using integers.

Tags:
lowres / low-res / Pixelart / resolution / Mosaic / tiling / old-school / retro / grid

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Color** (Vector4) | Multiplier color applied to the final output |
| **Divisor** (Int32) | Sets the size of the tiles according to the source resolution |
| **TileAmount** (Int2) | Set X and Y resolution (ignored if Divisor is greater than 0) |
| **Shape** (Texture2D) | Customize the tile, could be used to  |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

