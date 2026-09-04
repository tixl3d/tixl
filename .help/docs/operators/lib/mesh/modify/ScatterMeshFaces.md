# ScatterMeshFaces

*in [Lib.mesh.modify](README.md)*

This op is similar to [DisplaceMesh]. However, it uses face centers instead of the vertices to "scatter" a mesh while keeping the size of the individual faces.

Please note that this effect works best with flat shader meshes created by [SplitMeshVertices], because this operator will generate artifacts for meshes that use shared vertices.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputMesh** (MeshBuffers) | — |
| **Direction** (Int32) | — |
| **Amount** (Single) | — |
| **Rotate** (Single) | — |
| **Push** (Single) | — |
| **Shrink** (Single) | — |
| **Scatter** (Single) | — |
| **Distort** (Single) | — |
| **NoiseAmount** (Single) | — |
| **NoiseFrequency** (Single) | — |
| **NoisePhase** (Single) | — |
| **NoiseVariation** (Single) | — |
| **UseVertexSelection** (Boolean) | — |
| **AmountDistribution** (Vector3) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshBuffers |

