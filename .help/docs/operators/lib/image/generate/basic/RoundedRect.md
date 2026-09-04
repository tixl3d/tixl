# RoundedRect

*in [Lib.image.generate.basic](README.md)*

Generates a rounded rectangle.

Can be customized to create a variety of shapes, similarly to [Blob].

AKA: rectangle, square, ellipse, circle, rings, blob, vignette

Tips:

Similar Ops: [NGon] [RoundedRect] [Rings] [RadialGradient] [LinearGradient] [Blob]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | Image to use as a background for the shape.<br/>Drawn behind the background color. |
| **Color** (Vector4) | Fill color of the shape. |
| **Background** (Vector4) | Background color of the shape. |
| **Position** (Vector2) | Center position. |
| **Stretch** (Vector2) | Stretches the shape non-uniformly. |
| **Scale** (Single) | Scales the shape uniformly. |
| **Rotate** (Single) | Rotates the shape. |
| **Round** (Single) | Rounds corners of the shape.<br/>A value of 0 will create sharp corners, and 1 will create a pill shape. |
| **StrokeColor** (Vector4) | Color used by the shape stroke. |
| **Stroke** (Single) | Stroke (outline) thickness. |
| **Feather** (Single) | Feather edges to reduce pixel artifacts.<br/>Can also be used to blur the shape.<br/>Set to a negative value to create a vignette. |
| **FeatherBias** (Single) | Weights the feathering towards one edge or the other of the blurred region. |
| **Resolution** (Int2) | Output resolution in pixels. Set to 0 for dynamic resolution. |
| **GenerateMips** (Boolean) | Generate mipmaps (scaled-down versions of this image for use in situations where many small copies are shown on screen.)<br/>Will increase memory usage. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

