# BoxGradient

*in [Lib.image.generate.basic](README.md)*

A box gradient using the signed distance field "Box - Exact" described in the "2D distance functions" article on Inigo Quilez's website. 

Based on [RadialGradient]

2D, SDF, sdBox, [RoundedRect]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **Rotation** (Single) | — |
| **Center** (Vector2) | — |
| **Size** (Vector2) | — |
| **UniformScale** (Single) | — |
| **CornersRadius** (Vector4) | top left, top right, bottom right, bottom left |
| **Gradient** (Gradient) | — |
| **GradientWidth** (Single) | — |
| **Offset** (Single) | — |
| **PingPong** (Boolean) | — |
| **Repeat** (Boolean) | — |
| **GainAndBias** (Vector2) | — |
| **BlendMode** (Int32) | — |
| **Resolution** (Int2) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

