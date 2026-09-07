# CubeMesh

*in [Lib.mesh.generate](README.md)*

Generates a procedural three-dimensional mesh which can be rendered with [DrawMesh], [DrawMeshUnlit] and [DrawMeshHatched] among others.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToDrawThings].

It can also become the source for a particle system with [MeshVerticesToPoints] or [PointsOnMesh].
To import static 3D meshes from other programs refer to [LoadObj].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Segments** (Int3) | Amount of segments from left to right / top to bottom / front to back. |
| **Stretch** (Vector3) | Scales the size X (width) Y (height) Z (depth) |
| **Scale** (Single) | Uniformly scales the mesh |
| **Pivot** (Vector3) | Offsets the position of the pivot point (center).<br/><br/>This is helpful to change the point around which the cube rotates / along which it is scaled. |
| **Center** (Vector3) | Transforms the position of the pivot |
| **Rotation** (Vector3) | Rotates the Mesh |
| **TexCoord** (Int32) | Alternative UV Maps |
| **Margin** (Single) | Padding between UV islands |
| **TexCoord2** (Int32) | Alternative UV Maps |
| **Margin2** (Single) | Padding between UV islands |

## Outputs
| Name | Type |
|---|---|
| **Data** | T3.Core.DataTypes.MeshBuffers |

