# ScaleResolution

*in [Lib.numbers.int2.process](README.md)*

Non-uniformly scales/multiplies an int2 which is used for defining resolutions

For a similar operator with fewer settings see [ScaleSize]

Also see [MakeResolution], [int2], etc.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Resolution** (Int2) | Defines X and Y / Width and height of the resolution to be scaled |
| **Factor** (Vector2) | Defines the scaling factor for X and Y / Width and height |
| **ClampToValidTextureSize** (Boolean) | Can limit the output to resolutions that are valid for textures (e.g. powers of 2) |

## Outputs
| Name | Type |
|---|---|
| **Size** | T3.Core.DataTypes.Vector.Int2 |

