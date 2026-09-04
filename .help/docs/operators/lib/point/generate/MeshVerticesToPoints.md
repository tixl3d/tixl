# MeshVerticesToPoints

*in [Lib.point.generate](README.md)*

Creates a point at each vertex of the connected mesh.
Intended as a helper method to analyze meshes.

Useful combinations [LoadObj], [DrawPoints] and grouped with [DrawMesh] with Fillmode set to 'Wireframe' (similar to [VisualizeMesh])

Similar Operators:
To create a point at each face center of the connected mesh [MeshFacesPoints] can be used.
To get evenly distributed points on a mesh [PointsOnMesh] can be used.

 

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | Mesh Input via [LoadObj] |
| **W** (Single) | Defines W / the size of the points. |
| **OffsetByTBN** (Vector3) | Moves the points along one of the axes of the normal |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

