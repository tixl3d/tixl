# RecomputeNormals

*in [Lib.mesh.modify](README.md)*

Recalculates the normals (smoothing groups) of the surfaces of a mesh. For example, if these are changed by [DeformMesh] and similar operators

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputMesh** (MeshBuffers Required) | Mesh Input |
| **RecomputeIndices** (Boolean) | Defines whether the indices should also be recalculated |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshBuffers |

