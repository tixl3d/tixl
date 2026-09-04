# MovePointsToCurveSpace

*in [Lib.point.transform](README.md)*

An advanced op that transforms points into a curved space defined by another set of points.

To understand what this op does, imagine a set of points within a "source space" of a certain extent and orientation. This operator bends the content of that space along a wire defined by a set of target points.
After that, the source axis will be located on the line.

This op requires a careful setup of the orientation of target points! Use [VisualizePoints] to make sure that the target points face with the z-axis along the line (i.e., "forward" or "tangential").
You can use the [ResampleLinePoints] operator to recompute the orientation.

Similar to [MoveMeshToLinePoints]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SourcePoints** (BufferWithViews Required) | The points that will be transformed. |
| **TargetPoints** (BufferWithViews Required) | A point set that defines the target curve. These points MUST be oriented with their Z-axis along the line! |
| **Extent** (Vector3) | The extent of the source volume. Increasing the extent will shrink the resulting geometry.<br/>Enable gizmo visualization to see it. |
| **Pivot** (Vector3) | An optional offset on the source space. |
| **SourceAlignment** (Int32) | The axis that will become the center of the curve. |
| **Range** (Single) | The range of the curve that will be used for mapping. |
| **Offset** (Single) | Slides the points along the curve. This is similar to adjusting the corresponding axis of the Pivot parameter. |
| **Scale** (Single) | Scale of the resulting space. |
| **AtBoundaries** (Int32) | Defines what happens outside the source space bounds. |
| **Visibility** (GizmoVisibility) | — |

## Outputs
| Name | Type |
|---|---|
| **ResultPoints** | T3.Core.DataTypes.BufferWithViews |

