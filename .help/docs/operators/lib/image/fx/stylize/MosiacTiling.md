# MosiacTiling

*in [Lib.image.fx.stylize](README.md)*

Subdivides the incoming image into recursive tiles. 
This effect looks like the resolution of the image is reduced, also known as 'pixelate' or 'resizing without sampling'.

Check out the presets for an overview.

For similar effects or interesting combinations see: [MosaicTiling] [VoronoiCells] [SubdivisionStretch] [HoneyCombTiles] [TriangleGridTransition] [Dither] [AsciiRender]

Tags:
Pixelate / pixilated / Pixel / resolution / lowres / low-res / Pixelart / resolution / Mosaic

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **FxTextures** (Texture2D) | Image input |
| **Center** (Vector2) | Shifts the position of the effect over the input image |
| **Stretch** (Vector2) | Defines the stretching (currently not evaluated) |
| **Size** (Single) | Defines the scaling of the tiles |
| **SubdivisionThreshold** (Single) | Defines the threshold value from which a tile is further subdivided |
| **MaxSubdivisions** (Int32) | Defines the maximum amount of subdivisions |
| **Randomize** (Single) | Randomizes the threshold for subdivisions |
| **Padding** (Single) | Defines the thickness of the lines between the tiles |
| **Feather** (Single) | Defines the sharpness of the lines between the tiles |
| **GapColor** (Vector4) | Defines the color and alpha of the lines between the tiles |
| **MixOriginal** (Single) | Multiplies the color of the original image with the result |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

