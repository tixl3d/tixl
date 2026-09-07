# DrawRibbons

*in [Lib.point.draw](README.md)*

Draws a point buffer as ribbons. The lines will distance to the camera. You can override this with the ScaleWithDistance parameter. 
We use the points W attribute as a scale factor for the line width. 
If the W attribute of a point is NaN (Not a Number), that point is not being drawn and acts as a separator between the adjacent line segments.

Also see [DrawPoints], [DrawLines] and [DrawBillboards].
For a detailed interactive Tutorial about Tools Renderpipeline see [HowToDrawThings].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | Input for Points |
| **Color** (Vector4) | Selects the color for the ribbons. |
| **Width** (Single) | Width of the Ribbons |
| **Spin** (Single) | Uniformly rotates the ribbon around its center |
| **Twist** (Single) | Rotates the end point of the Ribbon and thus twists it. |
| **TextureMode** (Int32) | Defines the mode in which the texture is mapped to the tape.  |
| **TextureRange** (Vector2) | Modifies the Start- and Endpoint of the used texture scaling / stretching it. |
| **EnableDepthWrite** (Boolean) | Defines whether Ribbons cover themselves or are covered by or cover other 3D elements. |
| **BlendMod** (Int32) | Selects the Blendmode. |
| **Culling** (CullMode) | Defines the transparency of the surface.<br/>None: All surfaces are "bothsided" or always visible from all sides<br/>Front: The normals are flipped (frontside is transparent / backside visible)<br/>Back: Default (Frontside is visible / backside is invisible) |
| **UseWAsWeight** (Boolean) | Defines whether the "W" value of the points used has an influence on the width of the ribbon. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

