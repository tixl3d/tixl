# TransformPoints

*in [Lib.point.transform](README.md)*

Transforms incoming points.

Tips:
- Try to activate .WIsWeight and combine this operator with [SelectPoints].
- Changing the Space to Point can be used to offset the points.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Space** (Int32) | Defines the space |
| **Translation** (Vector3) | Moves the incoming points<br/><br/>X (-left / +right), <br/>Y (-down / +up), <br/>Z (-forward / +backwards)<br/> |
| **Rotation** (Vector3) | Rotates the incoming subgraph around the following axes:<br/><br/>X: Horizontal axis<br/>Y: Vertical axis<br/>Z: Forward axis<br/> |
| **Stretch** (Vector3) | Scales the incoming subgraph in the following directions:<br/><br/>X: Width<br/>Y: Height<br/>Z: Depth<br/> |
| **Scale** (Single) | Uniformly scales the incoming subgraph |
| **UpdateRotation** (Boolean) | Defines if the rotation of the points is ignored |
| **Shearing** (Vector3) | Shears the incoming points<br/><br/>X (-left / +right), <br/>Y (-down / +up), <br/>Z (-forward / +backwards)<br/> |
| **Pivot** (Vector3) | Moves the Pivot (center point) of the incoming subgraph:<br/><br/>X (-left / +right) <br/>Y (-down / +up) <br/>Z (-forward / +backwards)<br/><br/>The Pivot Point determines the location of the incoming subgraph Gizmo. Transforming its location can make it easier to perform transformations around the position you want.<br/> |
| **Strength** (Single) | — |
| **StrengthFactor** (Int32) | — |
| **ScaleW** (Single) | Scales the W of the incoming points |
| **OffsetW** (Single) | Defines the value that is added/subtracted from W |
| **WIsWeight** (Boolean) | Defines if points with different Ws are treated differently |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

