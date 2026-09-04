# TransformMeshUVs

*in [Lib.mesh.modify](README.md)*

Manipulates the existing UV coordinates of a mesh to change the mapping of textures.

Becomes visible with [DrawMesh].

To overwrite existing UV Coordinates with planar mapping see [MeshProjectUV]

Also see [ReprojectToUV].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputMesh** (MeshBuffers Required) | — |
| **Translate** (Vector3) | Moves the UV coordinates linearly |
| **Rotate** (Vector3) | Rotates the UV coordinates |
| **Stretch** (Vector3) | Scales the UV coordinates linearly per axis |
| **Uniformscale** (Single) | Scales the UV coordinates linearly |
| **UseVertexSelection** (Boolean) | Defines whether only selected vertices should be affected by the manipulation |
| **Pivot** (Vector3) | Moves the center point of the UV manipulations for rotations or scaling |
| **TexCoord2** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshBuffers |

