# DrawLines

*in [Lib.point.draw](README.md)*

Draws a point buffer as lines. The lines will be aligned to the camera, but their width will shrink with distance to the camera. You can override this with the ScaleWithDistance parameter. We use the point’s W attribute as a scale factor for the line width. If the W attribute of a point is NaN (Not a Number), that point is not being drawn and acts as a separator between the adjacent line segments. This allows a point buffer to contain multiple disconnected line segments.
DrawLines ignores the orientation defined by the points. Consider using [DrawRibbons] if you need consistent orientation.

Some caveats:
---------------
Long line segments that intersect the camera plane (i.e., one point is in front of the camera and the other behind the camera) will not be rendered correctly. Please consider using [DrawRaypoints] for this.

Performance considerations:
---------------
Drawing lines is fast. Depending on your graphics card, you can draw millions of lines per frame. Performance will be impacted by the length of your lines rather than the point count. This is directly related to your render target’s resolution and multi-sampling level (MSAA).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | Input for GPoints |
| **Color** (Vector4) | Defines the color of the Lines.<br/>If a Texture is used this color will be multiplied. |
| **LineWidth** (Single) | Defines the width |
| **WidthFactor** (Int32) | — |
| **ShrinkWithDistance** (Single) | — |
| **EnableZTest** (Boolean) | Defines whether Lines cover themselves or are covered by or cover other 3D elements. |
| **Texture_** (Texture2D) | Texture Input. |
| **BlendMod** (Int32) | Selects the Blendmode |
| **EnableZWrite** (Boolean) | Defines whether Lines cover themselves or are covered by or cover other 3D elements. |
| **TransitionProgress** (Single) | — |
| **WrapMode** (TextureAddressMode) | Defines how the texture behaves when repeated.<br/><br/>Wrap: The texture repeats itself continuously<br/>Mirror: The texture is mirrored and repeats infinitely.<br/>Clamp: The texture is cut off at the edge<br/>Border: Unclear<br/>MirrOnce: The texture is mirrored once, then cut off |
| **UvScale** (Single) | Uniformly scales the mapping of the used texture |
| **UvScaleFX** (Int32) | — |
| **FadeOutTooLong** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

