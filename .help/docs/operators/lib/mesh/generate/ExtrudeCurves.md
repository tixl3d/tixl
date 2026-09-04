# ExtrudeCurves

*in [Lib.mesh.generate](README.md)*

Generates a mesh by extruding a set of points along a set of other points.

The extrusion is done along the z-axis. This means that the rail points should be aligned in a way that z is pointing along the rail.

Also, the normals are directly taken from the extruded points' z-axis. You can use the [VisualizePoints] to verify the alignment and adjust it in [TransformPoints] in point space.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **RailPoints** (BufferWithViews Required) | — |
| **ProfilePoints** (BufferWithViews Required) | — |
| **Width** (Single) | — |
| **ScaleFactor** (Int32) | — |
| **UseExtend** (Boolean) | — |
| **UVsDirection** (Boolean) | — |
| **UseWAsWidth** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output2** | T3.Core.DataTypes.MeshBuffers |

