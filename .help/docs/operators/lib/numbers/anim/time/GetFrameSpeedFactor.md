# GetFrameSpeedFactor

*in [Lib.numbers.anim.time](README.md)*

This is set when rendering updates not at 60fps. This can happen for...

- high framerate displays -> e.g. 2 for 120Hz displays
- when rendering low fps image sequences -> e.g. 25/60 for 25fps
   
If possible, simulation operators like [ParticleSystem] or [FeedbackEffect] should apply this factor to their overall speed factor.

## Outputs
| Name | Type |
|---|---|
| **FrameSpeedFactor** | System.Single |

