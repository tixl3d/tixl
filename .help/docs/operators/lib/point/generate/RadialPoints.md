# RadialPoints

*in [Lib.point.generate](README.md)*

A versatile generator of circular point sets that can create a variety of circles, spirals, helixes, etc.

Try the presets to get an overview.

Needs a [DrawPoints], [DrawLines] or [DrawMeshAtPoints] or similar in order to become visible.

Similar: [GridPoints], [SpherePoints], [PointsOnMesh]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Count** (Int32) | Amount of points generated between the first and last point |
| **Radius** (Single) | Radius of radial |
| **OffsetRadius** (Single) | This value defines how far the radius of the last point deviates from the radius of the first point.<br/><br/>Especially helpful if Cycles are increased |
| **Center** (Vector3) | Moves the points:<br/><br/>X (-left / +right), <br/>Y (-down / + up), <br/>Z (-forward / +backwards) |
| **Axis** (Vector3) | — |
| **OffsetCenter** (Vector3) | Moves the position of the last point in relation to the first point:<br/><br/>X (-left / +right), <br/>Y (-down / + up),<br/>Z (-forward / +backward)<br/> |
| **StartAngle** (Single) | — |
| **Rotations** (Single) | — |
| **GainAndBias** (Vector2) | — |
| **CloseCircleLine** (Boolean) | When drawing the radial points as a closed line, two additional points have to be appended: One at the start position and one as separator.<br/><br/>It will add an overlapping point and a separator, so the number of corners will be Count - 2. |
| **Scale** (Vector2) | The first value will be default uniform scale factor. <br/>The y component is and offset to that at the end of the point. |
| **F1** (Vector2) | The first value will be default Fx1 value. <br/>The y component is and offset to that at the end of the point. |
| **F2** (Vector2) | The first value will be default Fx2 value. <br/>The y component is and offset to that at the end of the point. |
| **Color** (Vector4) | — |
| **OrientationAxis** (Vector3) | — |
| **OrientationAngle** (Single) | — |
| **OrientationMode** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

