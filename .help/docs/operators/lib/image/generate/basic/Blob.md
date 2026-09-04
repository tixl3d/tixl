# Blob

*in [Lib.image.generate.basic](README.md)*

Generates ellipses, circles, blobs, vignettes, and similar shapes.

Updated version of [_BlobOld] with slightly different functionality: uses an ellipse as a base rather than a rounded rectangle.
For other behaviours, consider [RoundedRect] or [RadialGradient].

Tips:
- Set "Feather" to a negative value to create a vignette.
- The background color is still visible on feathered edges when it's set to transparent.
- For more information on mipmaps, see the attached link about realtime rendering in TiXL.


Similar Ops: [NGon] [RoundedRect] [Rings] [RadialGradient] [LinearGradient] [Blob]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | Image to use as a background for the blob.<br/>Drawn behind the background color. |
| **Color** (Vector4) | Fill color of the blob. |
| **Background** (Vector4) | Background color of the blob. |
| **BlendMode** (Int32) | Blend mode between the generated blob graphic and the background image, if one is provided. |
| **Scale** (Single) | Scales the blob evenly. |
| **Stretch** (Vector2) | Stretches the blob unevenly. |
| **Rotate** (Single) | Rotation amount in degrees.<br/>Rotation is applied after Stretch and Scale, but before Position. |
| **Feather** (Single) | Feather edges to reduce pixel artifacts.<br/>Can also be used to blur the blob.<br/>Set to a negative value to create a vignette. |
| **FeatherBias** (Single) | Weights the feathering towards one edge or the other of the blurred region. |
| **Position** (Vector2) | X/Y position, in relative units. |
| **GenerateMips** (Boolean) | Generate mipmaps (scaled-down versions of this image for use in situations where many small copies are shown on screen.)<br/>Will increase memory usage. |
| **Resolution** (Int2) | Output resolution in pixels. Set to 0 for dynamic resolution. |
| **TextureFormat** (Format) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

