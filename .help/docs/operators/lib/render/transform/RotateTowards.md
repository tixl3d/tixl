# RotateTowards

*in [Lib.render.transform](README.md)*

Rotates the incoming scene / objects towards a specified target.

Also known as 'LookAt'

Useful combination: Using the position output of a [Locator] creates a visible Target in the viewport with an interactive transform gizmo (if 'toggle gizmos and floor grid' are turned on in the Output window)

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command Required) | Scene / Object input that will be rotated to look towards the set target |
| **AlternativeTarget** (Vector3) | Input for a 'Pos' value<br/>For example from a [Locator] node. |
| **RotationOffset** (Vector3) | Values to offset the rotation of the incoming scene / object |
| **LookTowards** (Int32) | Defines whether the incoming scene turns towards the active camera or towards the defined 'AlternativeTarget' |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

