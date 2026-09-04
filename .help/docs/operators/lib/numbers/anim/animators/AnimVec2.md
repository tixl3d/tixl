# AnimVec2

*in [Lib.numbers.anim.animators](README.md)*

Generates a repetitive LFO-like signal synced to the current BPM rate. It supports various shapes, modes, and forms.

You can manipulate the rate and shape directly in the graph by dragging with CTRL+ left mouse button.

Some tips:
- Please check out the [HowToAnimate] playground to see some applications.
- You can use [SetSpeedFactors] to override the incoming rate, which is very useful for live VJ setups.

- Also have a look at [AnimValue], [AnimVec3], [TriggerAnim], [OscillateVec2], and [OscillateVec3]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **OverrideTime** (Single) | — |
| **Shape** (Int32) | — |
| **Rates** (Vector2) | — |
| **RateFactor** (Single) | — |
| **Phases** (Vector2) | — |
| **Amplitudes** (Vector2) | — |
| **AmplitudeFactor** (Single) | — |
| **Offsets** (Vector2) | — |
| **Bias** (Single) | — |
| **Ratio** (Single) | — |
| **AllowSpeedFactor** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Numerics.Vector2 |

