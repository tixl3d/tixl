# LoadSvgAsTexture2D

*in [Lib.image.generate.load](README.md)*

Loads a SVG file as a Texture2D.
Note: This is a rasterization of a SVG (Scalable Vector Graphics).
You shall not animate its parameters. 


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Path** (String) | — |
| **Resolution** (Int2) | If width or height is zero, the image is scaled preserving the aspect ratio, if both are zero it'll use requested resolution height and keep aspect ratio.  |
| **UseViewBox** (Boolean) | — |
| **Scale** (Single) | Works on viewbox resolution |
| **SplitToLayers** (Boolean) | Attempt to access the different layers of your vector design |
| **SelectLayerRange** (Int2) | 0,1 means layers at index 0 and 1 (first two layers)<br/>1,5 means layers at indices 1,2,3,4,5 |

## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |

