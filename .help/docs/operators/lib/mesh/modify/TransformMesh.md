# TransformMesh

*in [Lib.mesh.modify](README.md)*

Generates a new set of transformed vertices for a mesh.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | — |
| **Translation** (Vector3) | Moves the incoming mesh<br/><br/>X (-left / +right), <br/>Y (-down / +up), <br/>Z (-forward / +backwards)<br/> |
| **Rotation** (Vector3) | Rotates the incoming mesh around the following axes:<br/><br/>X: Horizontal axis<br/>Y: Vertical axis<br/>Z: Forward axis<br/> |
| **Scale** (Vector3) | Scales the incoming subgraph in the following directions:<br/><br/>X: Width<br/>Y: Height<br/>Z: Depth<br/> |
| **UniformScale** (Single) | Uniformly scales the incoming mesh |
| **UseVertexSelection** (Boolean) | Defines whether only selected vertices should be affected by the manipulation |
| **Pivot** (Vector3) | Moves the pivot (center point) of the incoming subgraph:<br/><br/>X (-left / +right) <br/>Y (-down / +up) <br/>Z (-forward / +backwards)<br/><br/>The pivot point determines the location of the incoming subgraph gizmo. Transforming its location can make it easier to perform transformations around the position you want.<br/> |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshBuffers |

