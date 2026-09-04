# OscillateVec3

*in [Lib.numbers.anim.animators](README.md)*

A helper that combines 3 sin waves into a vector

Similar Operators [OscillateVec2] [AnimValue] [AnimVec2] [AnimVec3]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SpeedFactor** (Single) | Increases the speed at which the curves are drawn |
| **OverrideTime** (Single) | Input to override the standard time |
| **Amplitude** (Vector3) | Increases the highest and lowest point of the curves |
| **AmplitudeScale** (Single) | Uniformly scales the amplitude of all curves |
| **Period** (Vector3) | Increases / decreases the time it takes to draw a curve for each curve |
| **Phase** (Vector3) | Moves the phase of the curves back and forth on the time axis |
| **Offset** (Vector3) | Increases/decreases the values of the curves on the vertical axis up and down |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Numerics.Vector3 |

