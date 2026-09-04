# DelaunayMesh

*in [Lib.mesh.generate](README.md)*

Generate a mesh from point list

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **BoundaryPoints** (StructuredList Required) | Defines the mesh boundaries |
| **ExtraPoints** (StructuredList) | works only if Fill density is set to 0 |
| **SubdivideLongEdges** (Single) | — |
| **FillDensity** (Single) | distribute points inside the mesh |
| **Seed** (Int32) | Point distribution seed |
| **Tweak** (Single) | remove points around the boundary shape |

## Outputs
| Name | Type |
|---|---|
| **Data** | T3.Core.DataTypes.MeshBuffers |

