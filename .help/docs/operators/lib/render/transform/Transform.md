# Transform

*in [Lib.render.transform](README.md)*

Moves, scales and rotates the sub graph. 
Transform ops can be chained to add local pivots.

Also see [HowToDraw3d] playground.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command Required) | — |
| **Translation** (Vector3) | Moves the incoming subgraph<br/><br/>X (-left / +right), <br/>Y (-down / +up), <br/>Z (-forward / +backwards)<br/> |
| **Rotation** (Vector3) | Rotates the incoming subgraph around the following axes:<br/><br/>X: Horizontal axis<br/>Y: Vertical axis<br/>Z: Forward axis<br/> |
| **Scale** (Vector3) | Scales the incoming subgraph in the following directions:<br/><br/>X: Width<br/>Y: Height<br/>Z: Depth<br/> |
| **UniformScale** (Single) | Uniformly scales the incoming subgraph |
| **Pivot** (Vector3) | Moves the pivot (center point) of the incoming subgraph:<br/><br/>X (-left / +right) <br/>Y (-down / +up) <br/>Z (-forward / +backwards)<br/><br/>The pivot point determines the location of the incoming subgraph gizmo. Transforming its location can make it easier to perform transformations around the position you want.<br/> |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

