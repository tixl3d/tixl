# MeshProjectUV

*in [Lib.mesh.modify](README.md)*

Overwrites the existing UV coordinates with planar mapping.

Becomes visible with [DrawMesh].

To manipulate existing UV Coordinates see [TransformMeshUVs].


Also see [ReprojectToUV].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers) | — |
| **Translate** (Vector3) | Moves the position of the planar mapping |
| **Rotate** (Vector3) | Rotates the planar mapping |
| **Stretch** (Vector3) | Scales the mapping of the individual axes |
| **Scale** (Single) | Uniformly scales the mapping |
| **ToTexCoord2** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.MeshBuffers |

