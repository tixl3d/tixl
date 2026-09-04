# PointsOnMesh

*in [Lib.point.generate](README.md)*

Get evenly distributed points on a mesh. Note that the initial evaluation of the mesh is extremely slow and should not be done on every frame.

Useful combinations [LoadObj] and [DrawPoints]

Similar Operators:
To create a point at each vertex of the connected mesh [MeshVerticesToPoints] can be used.
To create a point at each face of the connected mesh [MeshFacesPoints] can be used.

Similar: [GridPoints], [RadialPoints], [SpherePoints],

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | Mesh input via [LoadObj] for example |
| **Count** (Int32) | Defines the amount of points that are spawning on the surface |
| **Seed** (Single) | Seed to randomize the position of the points |
| **Texture** (Texture2D) | Defines the color of the points via an image operator like [LoadImage] for example |
| **UseVertexSelection** (Boolean) | Defines whether an operator set in between, such as [SelectVertices], is taken into account. |
| **IsEnabled** (Boolean) | Toggle on / off |

## Outputs
| Name | Type |
|---|---|
| **ResultPoints** | T3.Core.DataTypes.BufferWithViews |
| **Colors** | T3.Core.DataTypes.BufferWithViews |

