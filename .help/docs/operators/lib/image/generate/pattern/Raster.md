# Raster

*in [Lib.image.generate.pattern](README.md)*

Generates a wide range of patterns of dots and lines.

Connecting a texture to the Image input will allow the pattern to be influenced differently at different parts of the texture.

AKA: polkadot, pattern, grid, crosshair


Other interesting patterns can be generated with [SinForm] [ZollnerPattern] [FraserGrid] [Raster] [CheckerBoard] [RyojiPattern2] [RyojiPattern1]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | Adds an image to the background.<br/>This image can also be used to control other parameters dynamically. |
| **Offset** (Vector2) | Offsets the position of the raster texture.<br/>An offset of 1 is equivalent to the distance between dots. |
| **Rotate** (Single) | Rotates the pattern. Measured in degrees. |
| **Stretch** (Vector2) | Stretches the pattern unevenly. |
| **Scale** (Single) | Scales the pattern evenly. |
| **Color** (Vector4) | Main color of the raster pattern, used by dots and lines. |
| **Background** (Vector4) | Color of the background behind the raster pattern. |
| **MixOriginal** (Single) | Blends the image in with the background color.<br/>Note that the image will only be visible if the background color has transparency. |
| **Resolution** (Int2) | Output resolution. Set to 0 for dynamic scaling. |
| **DotSize** (Single) | Size of the dots in the pattern. |
| **LineWidth** (Single) | Width of the lines in the pattern. |
| **LineRatio** (Single) | At values below 0.5, creates a gap in the middle of the lines.<br/>At values above 0.5, creates a gap where lines intersect.<br/><br/>Uses a circular crop to create these gaps, so high line widths with a low ratio will cause the lines to look like circles. |
| **Feather** (Single) | Feathers (blurs) the raster pattern. Useful for smoothing out pixelated edges.<br/>Affected by the background color, even when it's transparent! |
| **RedToDotSize** (Single) | Strength of the input texture's red color channel on the dot size.<br/>Requires a texture to be connected to the Image input. |
| **GreenToLineWidth** (Single) | Strength of the input texture's green color channel on the line width.<br/>Requires a texture to be connected to the Image input. |
| **BlueToLineRatio** (Single) | Strength of the input texture's blue color channel on the line ratio.<br/>Requires a texture to be connected to the Image input. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

