# Text

*in [Lib.render.basic](README.md)*

Creates flat 2D Text as an Object in 3D Space from bitmap fonts.
For each character a quad mesh is generated. [TextSprites] can be used to manipulate these generated objects.

Any String Operator like [AString], [RandomString], etc. can be used as string input.

The fonts used consist of a BmFont font definition file and the matching MSDF (multichannel distance field) texture

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputText** (String) | String / Input for the text to be drawn |
| **Color** (Vector4) | Defines which color the surfaces of the letters have |
| **Shadow** (Vector4) | Defines the color / opacity of the shadow that appears as an outline<br/>Colors brighter than 1 create a glow effect |
| **Position** (Vector2) | Offsets the position of the Text element |
| **FontPath** (String) | Defines which font is used |
| **Size** (Single) | Uniformly scales the Text |
| **Spacing** (Single) | Defines how wide the gaps between the letters and words are |
| **LineHeight** (Single) | Defines the space between the lines, if there are more than 1 |
| **VerticalAlign** (Int32) | Defines how high the letters are in relation to the pivot.<br/><br/>Top: The pivot point is on the row line<br/>Middle: The letters are centered above the pivot<br/>Bottom: The pivot is at the bottom of the letters such as 'y', 'g', etc. |
| **HorizontalAlign** (Int32) | Defines where the pivot is located in the horizontal position |
| **CullMode** (CullMode) | Defines whether / which surface is rendered invisible |
| **EnableZTest** (Boolean) | Activates / deactivates whether the text covers / is covered by other elements. |
| **Sharpness** (Single) | — |
| **BillboardMode** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

