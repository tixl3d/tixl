# LoadImageFromUrl

*in [Lib.image.generate.load](README.md)*

Loads an image file with the specified URL. This process does not use caching.

To load an image from a local folder use [LoadImage]

Also see [RequestUrl]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Url** (String) | — |
| **TriggerUpdate** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |
| **ShaderResourceView** | SharpDX.Direct3D11.ShaderResourceView |

