# SetTime

*in [Lib.numbers.anim.time](README.md)*

Overrides the animation time of a sub-command graph.

Useful combination [PlayVideo] -> [Layer2d] -> [SetCommandTime]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SubTree** (Command Required) | Scene input |
| **NewTime** (Single Relevant) | Offsets the start time<br/>e.g. of a [PlayVideo] op when 'Offset mode' is set to 'Relative' |
| **OffsetMode** (Int32) | Defines how the time is offset |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

