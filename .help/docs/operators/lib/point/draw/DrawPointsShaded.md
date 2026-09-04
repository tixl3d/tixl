# DrawPointsShaded

*in [Lib.point.draw](README.md)*

Draws a point buffer as PBR-shaded spheres using the attributes defined by [SetMaterial].

Note: This operator was previously called "DrawMeshAsSpheres".

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | — |
| **PointSize** (Single) | — |
| **ScaleFactor** (Int32) | — |
| **UsePointScale** (Boolean) | — |
| **Color** (Vector4) | — |
| **EnableZWrite** (Boolean) | — |
| **EnableZTest** (Boolean) | — |
| **BlendMode** (Int32) | — |
| **FadeNearest** (Single) | — |
| **ColorField** (ShaderGraphNode) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

