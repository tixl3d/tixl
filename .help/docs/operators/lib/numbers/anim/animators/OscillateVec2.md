# OscillateVec2

*in [Lib.numbers.anim.animators](README.md)*

A helper that combines 2 sin waves into a vector

Similar Operators [OscillateVec3] [AnimValue] [AnimVec2] [AnimVec3]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **OverrideTime** (Single) | Input to override the standard time |
| **SpeedFactor** (Single) | Increases the speed at which the curves are drawn |
| **Amplitude** (Vector2) | Increases the highest and lowest points of the curves |
| **AmplitudeScale** (Single) | Uniformly scales the amplitude of all curves |
| **Period** (Vector2) | Increases/decreases the time it takes to draw a curve for each curve |
| **Phase** (Vector2) | Moves the phase of the curves back and forth on the time axis |
| **Offset** (Vector2) | Increases/decreases the values of the curves on the vertical axis up and down |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Numerics.Vector2 |

