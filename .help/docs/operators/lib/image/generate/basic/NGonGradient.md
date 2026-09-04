# NGonGradient

*in [Lib.image.generate.basic](README.md)*

Renders a polygon shape similar to [NGon], [Blob] or [RoundedRect].

Similar Ops: [Rings] [RadialGradient] [LinearGradient]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Position** (Vector2) | X/Y position, in relative units. |
| **Sides** (Single) | Defines the amount of sides |
| **Radius** (Single) | Defines the radius |
| **Curvature** (Single) | Defines if the outer line is convex or concave and can also invert the shape |
| **Roundness** (Single) | Defines how round / sharp the corners are |
| **Blades** (Single) | Offsets every second edge to create sharp corners |
| **Rotate** (Single) | Rotates the object around its center |
| **Gradient** (Gradient) | Defines the Gradient |
| **Width** (Single) | Defines the size of the gradient |
| **Offset** (Single) | Offsets the gradient inward / outward |
| **PingPong** (Boolean) | Mirrors the gradient inward |
| **Repeat** (Boolean) | Repeats the Gradient outward |
| **BiasAndGain** (Vector2) | Bias and gain to control the gradient |
| **BlendMode** (Int32) | Blend mode between the generated blob graphic and the background image, if one is provided. |
| **Resolution** (Int2) | Output resolution in pixels. Set to 0 for dynamic resolution. |
| **Image** (Texture2D) | An optional background image. If connected, allows applying different BlendingModes. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

