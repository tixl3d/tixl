# BlendMeshVertices

*in [Lib.mesh.modify](README.md)*

Blends between two sets of mesh vertices. This only yields meaningful (i.e., predictable) results for meshes with the same vertex count and topology.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **MeshA** (MeshBuffers Required) | Input for Mesh A |
| **MeshB** (MeshBuffers Required) | Input for Mesh A |
| **BlendValue** (Single) | Defines the transition between the two meshes<br/>0 = Mesh A is displayed without changes<br/>1 = Mesh B is displayed without changes<br/>If the 'BlendMode' is set to 'Blend', the vertices overshoot at values lower than 0 and higher than 1.<br/>If the 'BlendMode' is set to 'RangeBlendSmooth', the transition loops. |
| **Scatter** (Single) | Adds random noise to the vertices' position during the transition |
| **BlendMode** (Int32) | Defines how to blend between the meshes |
| **RangeWidth** (Single) | Defines the range width when the 'BlendMode' is set to 'RangeBlend' or 'RangeBlendSmooth' |
| **Pairing** (Int32) | Selects the mode with which vertices are paired for blending |

## Outputs
| Name | Type |
|---|---|
| **BlendedMesh** | T3.Core.DataTypes.MeshBuffers |

