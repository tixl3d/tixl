# SimCentricalOffset

*in [Lib.point.sim](README.md)*

Applies a directed force to points (acceleration stored in W). Use [SimForwardMovement] to move them as agents.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews) | Point input |
| **Center** (Vector3) | — |
| **MaxAcceleration** (Single) | — |
| **Amount** (Single) | — |
| **DecayExponent** (Single) | — |
| **ShowGizmo** (GizmoVisibility) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

