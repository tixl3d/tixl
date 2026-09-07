# Vector3Gizmo

*in [Lib.numbers.vec3](README.md)*

Creates a 3D Gizmo in the Output View (if gizmos are toggled on) and sends out the position data as a [Vector3].
Useful to control the position of anything in 3D space that does not have a built-in gizmo.
But can be used to control anything that takes a Vec3, RGB colors for example.

Similar to [Locator]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Position** (Vector3) | Defines the position |
| **ShowGizmo** (Boolean) | Toggles the visibility of the gizmo in the Output view |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Numerics.Vector3 |

