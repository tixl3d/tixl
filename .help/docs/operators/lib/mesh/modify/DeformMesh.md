# DeformMesh

*in [Lib.mesh.modify](README.md)*

Spherize, Taper and Twist. It works better if your mesh has a high density of vertices.
(such as a [CubeMesh] with 64 segments on each axis, for example)

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | — |
| **UseVertexSelection** (Boolean) | — |
| **Spherize** (Single) | — |
| **Radius** (Single) | — |
| **Pivot** (Vector3) | Pink cross, center of Spherize |
| **Taper** (Single) | — |
| **TaperAxis** (Int32) | — |
| **AmountPerAxis** (Vector2) | — |
| **Twist** (Single) | — |
| **TwistAxis** (Int32) | — |
| **TwistPivot** (Vector3) | Blue cross, origin of twist deformation |
| **ShowPivots** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshBuffers |

