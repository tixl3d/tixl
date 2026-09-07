# SelectVertices

*in [Lib.mesh.modify](README.md)*

Sets the selection property of mesh vertices from a volume. This can later be used to selectively apply manipulations like displace.

Also see: [ScatterMeshFaces]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputMesh** (MeshBuffers Required) | — |
| **VolumeShape** (Int32) | — |
| **Center** (Vector3) | — |
| **Stretch** (Vector3) | — |
| **Scale** (Single) | — |
| **Rotate** (Vector3) | — |
| **FallOff** (Single) | — |
| **Mode** (Int32) | — |
| **ClampResult** (Boolean) | — |
| **Strength** (Single) | — |
| **Phase** (Single) | — |
| **Threshold** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshBuffers |

