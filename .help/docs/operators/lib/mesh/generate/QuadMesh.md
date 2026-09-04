# QuadMesh

*in [Lib.mesh.generate](README.md)*

Generates a procedural three-dimensional tessellated mesh which can be rendered with [DrawMesh], [DrawMeshUnlit] and [DrawMeshHatched] among others.
Also known as: Plane, Quad

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToDrawThings].

It can also become the source for a particle system with [MeshVerticesToPoints] or [PointsOnMesh].
To import static 3D meshes from other programs refer to [LoadObj].

Also consider using [CylinderMesh].



## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Segments** (Int2) | Defines the tessellation<br/>X: Left to Right <br/>Y: Top to Bottom |
| **Stretch** (Vector2) | Controls the scaling:<br/>X: Width<br/>Y: Height |
| **Scale** (Single) | — |
| **Pivot** (Vector2) | Offsets the position of the pivot point (center).<br/><br/>This is helpful to change the point around which the Mesh rotates / along which it is scaled. |
| **Center** (Vector3) | Transforms the position of the pivot |
| **Rotation** (Vector3) | Rotates the Mesh |

## Outputs
| Name | Type |
|---|---|
| **Data** | T3.Core.DataTypes.MeshBuffers |

