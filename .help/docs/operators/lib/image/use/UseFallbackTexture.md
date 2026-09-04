# UseFallbackTexture

*in [Lib.image.use](README.md)*

Automatically replaces a non-loadable texture with a predefined backup. 
For example to define a backup if a [LoadImageFromUrl] cannot be reached due to a lack of Internet.

Needs a [LoadImage].

Useful combinations: [UnsplashAPI]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **TextureA** (Texture2D Required) | Main Texture input |
| **Fallback** (Texture2D Required) | Input for Backup / Fallback texture |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

