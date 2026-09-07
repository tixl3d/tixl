# TextureDisplaceMesh

*in [Lib.mesh.modify](README.md)*

Uses a projected texture with a normal map to displace the mesh. 

This is similar to [SamplePointAttributes].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | — |
| **Center** (Vector3) | — |
| **Stretch** (Vector2) | — |
| **Scale** (Single) | — |
| **TextureRotate** (Vector3) | — |
| **Amount** (Single) | — |
| **AmountDistribution** (Vector3) | — |
| **RotationSpace** (Int32) | — |
| **RotationLookupDistance** (Single) | — |
| **UseVertexSelection** (Boolean) | — |
| **TextureMode** (TextureAddressMode) | — |
| **Visibility** (GizmoVisibility) | — |
| **Texture** (Texture2D Required) | — |

## Outputs
| Name | Type |
|---|---|
| **DisplacedMesh** | T3.Core.DataTypes.MeshBuffers |

