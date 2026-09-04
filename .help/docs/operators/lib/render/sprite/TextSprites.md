# TextSprites

*in [Lib.render.sprite](README.md)*

Creates Points and Sprites from text that can be animated and then rendered with [DrawPointSprites] or [DrawPointSpritesShaded].

This operator is an advanced solution that loads a BmFont font definition and the matching MSDF (multichannel distance field) texture and generates a set of buffers.
These buffers can then be modified and combined with other operators.

The Point buffer includes position, orientation, Color, F1, F2, Scale (XYZ).
The Sprite buffer contains additional attributes for rendering like texture UV coordinates for characters, pivot, size, and layout information.

Some tips:
- You might replace the Point buffer with another buffer (e.g., [RadialPoints]).
- For rendering basic text usage, consider using [Text].

Check the examples.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Text** (String) | — |
| **Filepath** (String) | — |
| **Color** (Vector4) | — |
| **Size** (Single) | — |
| **Spacing** (Single) | — |
| **LineHeight** (Single) | — |
| **Position** (Vector3) | — |
| **VerticalAlign** (Int32) | — |
| **HorizontalAlign** (Int32) | — |
| **OffsetBaseLine** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **PointBuffer** | T3.Core.DataTypes.BufferWithViews |
| **SpriteBuffer** | T3.Core.DataTypes.BufferWithViews |
| **Texture** | T3.Core.DataTypes.Texture2D |

