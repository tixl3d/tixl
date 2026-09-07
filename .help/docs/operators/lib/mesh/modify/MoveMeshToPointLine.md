# MoveMeshToPointLine

*in [Lib.mesh.modify](README.md)*

Deforms a mesh in world space origin along a line defined by points.

It maps the geometry to the complete range of all points. Use the range parameter to squeeze the geometry to the correct ratio.
Use the Offset parameter to shift the object along the line.
The Scale parameter can help adjust the overall scale of the geometry.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputMesh** (MeshBuffers Required) | — |
| **Points** (BufferWithViews Required) | — |
| **Range** (Single) | — |
| **Offset** (Single) | — |
| **Scale** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshBuffers |

