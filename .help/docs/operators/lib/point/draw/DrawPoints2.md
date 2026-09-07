# DrawPoints2

*in [Lib.point.draw](README.md)*

A new version of [DrawPoints] that uses a Radius parameter instead of Size.

It draws a point buffer with the set camera, transform, and fog. The points are drawn as camera-facing billboards, ignoring the point orientation. The W attribute of the points is used for scaling. This can be controlled with the UseWForSize parameter.

You can connect a texture to override the round shape.

There are many Operators allowing you to create and manipulate point buffers in countless ways. Please have a look at the examples listed below.

For additional effects, you can experiment with different blend modes or disable depth testing. Note, however, that the points are drawn in the order they are defined in the buffer and not sorted by distance to the camera. This can introduce unwanted artifacts with transparent blending.

Although drawing points is fast, drawing very large points can lead to high fill rates and can drastically impact render performance.

Also see: [HowToDrawThings], [HowToUsePoints]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | — |
| **Color** (Vector4) | — |
| **Radius** (Single) | — |
| **Texture_** (Texture2D) | — |
| **EnableZWrite** (Boolean) | — |
| **EnableZTest** (Boolean) | — |
| **BlendMode** (Int32) | — |
| **FadeNearest** (Single) | — |
| **UseWForSize** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

