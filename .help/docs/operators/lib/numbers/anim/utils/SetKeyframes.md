# SetKeyframes

*in [Lib.numbers.anim.utils](README.md)*

Allows to record keyframes to a connected animated operator

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **TriggerSet** (Boolean) | When to set keyframes.  Try using it with [AudioReaction].WasHit |
| **TriggerClear** (Boolean) | — |
| **Value** (Single Relevant) | The value that should be written to the keyframe |
| **OpIndex** (Int32) | — |
| **CurveIndex** (Int32) | — |
| **AnimatedOp** (Single Required) | NOTE: These must be animated! |

## Outputs
| Name | Type |
|---|---|
| **CurrentValue** | System.Single |

