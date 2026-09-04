# DampVec3

*in [Lib.numbers.vec3](README.md)*

Damps (i.e. smoothens or filters) an incoming float value.

Other damping functions: [Damp], [DampAngle], [DampVec2] and [DampFloatList].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Value** (Vector3) | Input value to be dampened.<br/> |
| **Damping** (Single) | Amount of damping to apply.<br/>The ideal setting for this depends on the range of the input values: Generally, lower damping works better with very large changes in value. |
| **Method** (Int32) | LinearInterpolation: Linear interpolate between the current and target value by the damping amount. Set Damping to 0 for no damping, and to 1 to freeze the current value (and never reach the target).<br/><br/>DampedSpring: Uses a "critically damped spring" to provide very smooth interpolation that will never overshoot. For reference, this method is used by Unity's SmoothDamp method. |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Numerics.Vector3 |

