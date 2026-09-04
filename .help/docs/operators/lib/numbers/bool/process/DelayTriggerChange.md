# DelayTriggerChange

*in [Lib.numbers.bool.process](README.md)*

Delays the change of a boolean flag. This can be useful for implementing interactions where a value needs to stay true for a minimum duration. In "DelayTrue" mode, it will immediately switch to true but delay switching back to false. Note: This is NOT a queue. Frequent changes of the incoming signal can lead to the delayed state filtering out changes within the delay duration. In vvvv, this op is called a MonoFlop.

Also see [DelayBoolean] if you want to delay a signal.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Trigger** (Boolean Required) | — |
| **DelayDuration** (Single) | — |
| **Mode** (Int32) | — |
| **TimeMode** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **DelayedTrigger** | System.Boolean |
| **RemainingTime** | System.Single |

