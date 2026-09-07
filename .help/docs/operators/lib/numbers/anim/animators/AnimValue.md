# AnimValue

*in [Lib.numbers.anim.animators](README.md)*

Generates a repetitive LFO-like signal synced to the current BPM rate. It supports various shapes, modes and forms.

You can manipulate the rate and shape directly in the graph by dragging with CTRL+ left mouse button.

Some tips:
- Please check out the [HowToAnimate] playground to see some applications.
- You can use [SetSpeedFactors] to override the incoming rate, which is very useful for live VJ setups.

- Also have a look at [AnimVec2], [AnimVec3], [TriggerAnim], [OscillateVec2] and [OscillateVec3]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **OverrideTime** (Single) | Overwrites the internal standard time, e.g. with a [time] operator |
| **Shape** (Int32) | Selects the form of the animation |
| **Rate** (Single) | Defines the speed |
| **Ratio** (Single) | Defines the ratio |
| **Phase** (Single) | Linear offset for the timing |
| **Amplitude** (Single) | Defines the strongest value of the animation curve |
| **Offset** (Single) | Linear offset for the value |
| **Bias** (Single) | Changes the Bias for the selected shape |
| **AllowSpeedFactor** (Int32) | Switches between SpeedFactors |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |
| **WasHit** | System.Boolean |

