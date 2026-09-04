# BlendMeshToPoints

*in [Lib.mesh.modify](README.md)*

Allows to deform a mesh with a point buffer, for example you can use [MeshVerticesToPoints] and apply a point modifier. 

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | Input for Mesh A |
| **Points** (BufferWithViews) | — |
| **BlendValue** (Single) | — |
| **Scatter** (Single) | Adds random noise to the vertices' position during the transition |
| **BlendMode** (Int32) | — |
| **RangeWidth** (Single) | Defines the range width when the 'BlendMode' is set to 'RangeBlend' or 'RangeBlendSmooth' |
| **Pairing** (Int32) | Selects the mode with which vertices are paired for blending |

## Outputs
| Name | Type |
|---|---|
| **BlendedMesh** | T3.Core.DataTypes.MeshBuffers |

