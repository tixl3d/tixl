# Damp

*in [Lib.numbers.float.process](README.md)*

Damps (i.e. smoothens or filters) an incoming float value.

See also: [DampVec2], [DampVec3] and [DampFloatList].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Value** (Single Required) | Input value to be dampened. |
| **Damping** (Single) | Amount of damping to apply.<br/>The ideal setting for this depends on the range of the input values: Generally, lower damping works better with very large changes in value. |
| **Method** (Int32) | LinearInterpolation: Linear interpolate between the current and target value by the damping amount. Set Damping to 0 for no damping, and to 1 to freeze the current value (and never reach the target).<br/><br/>DampedSpring: Uses a "critically damped spring" to provide very smooth interpolation that will never overshoot. For reference, this method is used by Unity's SmoothDamp method. |
| **UseAppRunTime** (Boolean) | Advanced setting. Checking this box will cause the damping calculations to use RunTime instead of FxTime for their timing. This means the damping shape/speed will not be affected by any changes you might make to playback speed (such as pausing or reversing the timeline). |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |

