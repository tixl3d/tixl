# IkChain

*in [Lib.point.transform](README.md)*

FABRIK inverse kinematic on the GPU. While this operator is still experimental, it works pretty good. Suggestions and help needed.
Use [Once] to initialize.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Tolerance** (Single) | — |
| **MaxIterations** (Int32) | — |
| **Reset** (Boolean) | Initialize the ik chain |
| **Targets** (BufferWithViews) | — |
| **Influence** (Single) | Targets influence |
| **PointsPerChain** (Int32) | Set this value for multiple inverse kinematic chains. |
| **AngleConstraint** (Int32) | — |
| **MaxBendAngle** (Single) | — |
| **TargetRotation** (Boolean) | The last point get the rotation of its target. |
| **DirectionBias** (Vector3) | The chain will bend towards this direction |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

