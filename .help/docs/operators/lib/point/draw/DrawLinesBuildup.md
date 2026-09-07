# DrawLinesBuildup

*in [Lib.point.draw](README.md)*

Renders incoming points as growing strokes. The points' W attribute encodes the U progress of the extension of the strokes.

This operator requires a texture with a [LinearGradient] that is transparent at the ends.

Another approach would be [SvgToPoints]->[PrepareSvgLineTransition]->[ListToBuffer]->[DrawLinesBuildup].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | — |
| **Color** (Vector4) | — |
| **LineWidth** (Single) | — |
| **ShrinkWithDistance** (Single) | — |
| **TransitionProgress** (Single) | — |
| **VisibleRange** (Single) | — |
| **Texture_** (Texture2D) | — |
| **EnableTest** (Boolean) | — |
| **EnableDepthWrite** (Boolean) | — |
| **BlendMod** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

