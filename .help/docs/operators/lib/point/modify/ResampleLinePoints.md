# ResampleLinePoints

*in [Lib.point.modify](README.md)*

*No description yet. Edit this operator's description in the TiXL editor to populate this page.*

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Count** (Int32) | The number of resulting points. |
| **RangeMode** (Int32) | — |
| **SampleRange** (Vector2) | — |
| **SmoothDistance** (Single) | The smoothing distances for sampling. E.g. 1 rounding over the neighbour points. 2 rounding across two neighbours, etc. |
| **Samples** (Int32) | The sample count used for smoothing out the result. <br/>Because higher values can have a significant performance impact, the max count is 10. |
| **Rotation** (Int32) | Defines how rotation of the interpolated points is computed. <br/>This rotation can be relevant when resulting points are used for instantiating or repeating other points.<br/><br/>In some situations, the rotation is already inconsistent, e.g. when the position of the points has been randomized without adjusting the rotation. In this case, recomputing the rotation from the line's tangent can be helpful. |
| **RotationUpVector** (Vector3) | When using the "Recompute" rotation mode, this will be used as the up-vector to compute the normal direction of the line.<br/>This basically defines how the line twists around its forward direction.<br/><br/>If the direction of the approaches the up vector, the result becomes unstable because of gimbal lock. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

