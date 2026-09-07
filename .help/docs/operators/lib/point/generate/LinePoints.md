# LinePoints

*in [Lib.point.generate](README.md)*

Define points from a source position to a direction.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Count** (Int32) | — |
| **Center** (Vector3) | Starting point. |
| **Direction** (Vector3) | The direction the line is being oriented. E.g. 0, 1, 0 would point the line upwards. |
| **Length** (Single) | A factor multiplied to the direction of the line. <br/>Direction of 2, 0, 0  × 1  and 1, 0, 0 × 2 are equivalent. |
| **Pivot** (Single) | Defines how the Line is centered.<br/><br/>0 - the line starts at center<br/>0.5 - the line is centered<br/>1.0 -  the last point of the line is at the Center. |
| **GainAndBias** (Vector2) | — |
| **Scale** (Vector2) | — |
| **F1** (Vector2) | — |
| **F2** (Vector2) | — |
| **ColorA** (Vector4) | Color at the start of the line (i.e. at the Center) |
| **ColorB** (Vector4) | — |
| **Orientation** (Int32) | Sadly there is no simple method to define the orientation of the points.<br/><br/>Simple: Ignores Line direction and uses the Orientation and Angle<br/>Up Vector: Orient X towards the direction. This can have unpredictable results when the line is pointing up or when using Orientation Angles. |
| **Twist** (Single) | Twist the point orientations around the direction axis. This can be useful if the line points are used as targets for repeating other elements, e.g., to build a spiral staircase. |
| **OrientationAxis** (Vector3) | — |
| **OrientationAngle** (Single) | — |
| **AddSeparator** (Boolean) | When combining multiple lines into a single buffer, checking this option will prevent connecting them. |
| **W** (Single) | Width at the beginning of the line. |
| **WOffset** (Single) | Delta added to the start width. |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

