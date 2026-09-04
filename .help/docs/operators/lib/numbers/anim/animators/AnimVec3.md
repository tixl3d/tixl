# AnimVec3

*in [Lib.numbers.anim.animators](README.md)*

Generates a repetitive LFO-like signal synced to the current BPM rate. It supports various shapes, modes and forms.

You can manipulate the rate and shape directly in the graph by dragging with CTRL+ left mouse button.

Some tips:
- Please check out the [HowToAnimate] playground to see some applications.
- You can use [SetSpeedFactors] to override the incoming rate, which is very useful for live VJ setups.

- Also have a look at [AnimValue], [AnimVec2], [TriggerAnim], [OscillateVec2] and [OscillateVec3]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **OverrideTime** (Single) | — |
| **Shape** (Int32) | — |
| **Rates** (Vector3) | — |
| **RateFactor** (Single) | — |
| **Phases** (Vector3) | — |
| **Amplitudes** (Vector3) | — |
| **AmplitudeFactor** (Single) | — |
| **Offsets** (Vector3) | — |
| **Bias** (Single) | — |
| **Ratio** (Single) | — |
| **AllowSpeedFactor** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Numerics.Vector3 |

