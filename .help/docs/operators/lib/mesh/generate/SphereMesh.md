# SphereMesh

*in [Lib.mesh.generate](README.md)*

Generates a procedural three-dimensional UV sphere mesh which can be rendered with [DrawMesh], [DrawMeshUnlit] and [DrawMeshHatched] among others.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToDrawThings].

It can also become the source for a particle system with [MeshVerticesToPoints] or [PointsOnMesh].
To import static 3D meshes from other programs refer to [LoadObj].



## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Radius** (Single) | Controls the size. |
| **Segments** (Int2) | Controls the quantity of segments.<br/>The first value controls the division along the equator of the sphere.<br/>The second value controls the division from pole to pole. |

## Outputs
| Name | Type |
|---|---|
| **Data** | T3.Core.DataTypes.MeshBuffers |

