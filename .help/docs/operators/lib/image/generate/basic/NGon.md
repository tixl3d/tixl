# NGon

*in [Lib.image.generate.basic](README.md)*

Renders a polygon shape similar to [Blob] or [RoundedRect].

Similar Ops: [NGon] [RoundedRect] [Rings] [RadialGradient] [LinearGradient] [Blob]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **Fill** (Vector4) | — |
| **Background** (Vector4) | — |
| **Sides** (Single) | Defines the amount of sides |
| **Radius** (Single) | Defines the radius |
| **Curvature** (Single) | Defines if the outer line is convex or concave and can also invert the shape |
| **Blades** (Single) | Offsets every second edge to create sharp corners |
| **Feather** (Single) | Defines how sharp the outer edge of the object is rendered |
| **Round** (Single) | Smoothes the whole shape |
| **FeatherBias** (Single) | Defines the bias for the feather |
| **Position** (Vector2) | X/Y position, in relative units. |
| **Rotate** (Single) | Rotates the object around its center |
| **Resolution** (Int2) | Output resolution in pixels. Set to 0 for dynamic resolution. |
| **BlendMode** (Int32) | Blend mode between the generated blob graphic and the background image, if one is provided. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

