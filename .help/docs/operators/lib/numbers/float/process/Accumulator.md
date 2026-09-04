# Accumulator

*in [Lib.numbers.float.process](README.md)*

Accumulates a value with the incoming rate.

Note that the increment is a rate that gets multiplied with the current frame duration: With an increment of 1 you have a full value change of 1 unit after one second.

Similar to [Time].
For a variant that does not count up linearly / smooth but in steps, see [Counter]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Increment** (Single) | Defines how quickly / intensively the accumulator counts up |
| **Accumulate** (Int32) | — |
| **Running** (Boolean) | — |
| **StartValue** (Single) | Defines the number with which the accumulator starts counting.<br/><br/>Notice: On-the-fly changes are only applied after the reset trigger has been used |
| **ResetTrigger** (Boolean) | Resets the accumulator |
| **Modulo** (Single) | Additional modulo that can be useful to make the values loop.<br/>This parameter is ignored when 0. |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |

