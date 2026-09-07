# FindClosestPointsOnMesh

*in [Lib.point.transform](README.md)*

Combines points and a mesh and finds the nearest neighbors between points and mesh.
Useful to visualize 'scan effects' and allows the display of complex details.

The example shows the necessary structure.

Needs Points such as [GridPoints], [SpherePoints] etc.
Needs a mesh such as [LoadObj], [TorusMesh] etc.

Becomes visible with [DrawPoints] and [DrawLines] or similar.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews) | — |
| **Mesh** (MeshBuffers Required) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

