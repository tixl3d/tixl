# LoadImage

*in [Lib.image.generate.load](README.md)*

Loads an image file as a Texture2D.

Some notes:
- Resources are cached, so accessing the same filename again will be much faster.
- An ImageBuffer will forward its size so that image FX operators will produce buffers of identical sizes.
- Mipmaps are created automatically.

Most image types are supported, but only in 8-bit.

To directly use an image from a URL use [LoadImageFromUrl]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Path** (String) | — |
| **CacheResources** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |

