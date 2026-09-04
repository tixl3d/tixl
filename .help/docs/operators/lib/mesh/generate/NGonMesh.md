# NGonMesh

*in [Lib.mesh.generate](README.md)*

Generates a procedural flat circular mesh with a joined center vertex which can be rendered with [DrawMesh], [DrawMeshUnlit] and [DrawMeshHatched] among others.
Also consider using [CylinderMesh].

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToDrawThings].

It can also become the source for a particle system with [MeshVerticesToPoints] or [PointsOnMesh].
To import static 3D meshes from other programs refer to [LoadObj].



## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Segments** (Int32) | Controls the tessellation. |
| **Radius** (Single) | Defines the Radius<br/> |
| **Stretch** (Vector2) | Scales the Mesh<br/>X: Width<br/>Y: Height |
| **Center** (Vector3) | Transforms the position of the pivot |
| **Rotation** (Vector3) | Rotates the Mesh |
| **TextureMode** (Int32) | Select different UV methods |

## Outputs
| Name | Type |
|---|---|
| **Data** | T3.Core.DataTypes.MeshBuffers |

