# DrawPoints

*in [Lib.point.draw](README.md)*

Draws a point buffer with the set camera, transform, and fog. The points are drawn as camera-facing billboards, ignoring the point orientation. The W attribute of the points is used for scaling. This can be controlled with the UseWForSize parameter.

You can connect a texture to override the round shape.

There are many Operators allowing you to create and manipulate point buffers in countless ways. Please have a look at the examples listed below.

For additional effects, you can experiment with different blend modes or disable depth testing. Note, however, that the points are drawn in the order they are defined in the buffer and not sorted by distance to the camera. This can introduce unwanted artifacts with transparent blending.

Although drawing points is fast, drawing very large points can lead to high fill rates and can drastically impact render performance.

Some notes regarding the size: For historic reasons we apply a 1/10 factor on the size: A point with W=1 will have a radius of 0.01 world units. So a scale of 5 draws a point with a 1 unit diameter.


Also see: [HowToDrawThings], [HowToUsePoints], [DrawLines], [DrawRibbons].
For an Operator with more Options see [DrawBillboards].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | Input for Points |
| **PointSize** (Single) | Defines the size of the Points |
| **ScaleFactor** (Int32) | — |
| **UsePointsScale** (Boolean) | — |
| **Color** (Vector4) | Defines the color of the Points.<br/>If a Texture is used this color will be multiplied. |
| **BlendMode** (Int32) | Defines the Blendmode. |
| **AlphaCutOff** (Single) | — |
| **FadeNearest** (Single) | Points closer to the camera fade out. |
| **EnableZWrite** (Boolean) | Defines whether points cover themselves or are covered by or cover other 3D elements. |
| **EnableZTest** (Boolean) | Defines whether points cover themselves or are covered by or cover other 3D elements. |
| **Texture_** (Texture2D) | Input for a texture map |
| **ColorField** (ShaderGraphNode) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

