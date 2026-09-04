# SimDirectionalOffset

*in [Lib.point.sim](README.md)*

Linearly offsets the incoming points along the defined axis.

Needs a [DrawPoints], [DrawLines] or [DrawMeshAtPoints] or similar in order to become visible.

Needs Points as a base, for example: [RadialPoints], [SpherePoints], [PointsOnMesh]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews) | Point input |
| **Direction** (Vector3) | Defines how much the Points are offset on the given axis |
| **Amount** (Single) | Defines the strength of the force |
| **RandomAmount** (Single) | Randomizes the force per point |
| **Mode** (Int32) | Selects the mode |
| **ShowGizmo** (GizmoVisibility) | Defines if and how the gizmo is shown in the Output View |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

