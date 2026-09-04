# FindKeyframes

*in [Lib.numbers.anim.utils](README.md)*

Experimental operator to access keyframe times of another operator instance. This can be useful to build interactive use cases that rely on setting the playback time.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mode** (Int32) | — |
| **IndexOrTime** (Single) | — |
| **WrapIndex** (Boolean) | — |
| **OpIndex** (Int32) | — |
| **CurveIndex** (Int32) | — |
| **AnimatedOp** (Single Required) | — |

## Outputs
| Name | Type |
|---|---|
| **Time** | System.Single |
| **Value** | System.Single |
| **KeyframeCount** | System.Int32 |

