# HasValueChanged

*in [Lib.numbers.float.logic](README.md)*

Tests whether the incoming value has changed by a defined threshold and outputs this as a Boolean

Similar to [HasValueIncreased]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Value** (Single) | Value to be tested |
| **Threshold** (Single) | If the value has changed more than is defined here, the output boolean will be set accordingly |
| **Mode** (Int32) | Defines how the value is tested for the boolean to be 'True' |
| **MinTimeBetweenHits** (Single) | Timing threshold |
| **PreventContinuedChanges** (Boolean) | Can prevent unstable results in certain scenarios |

## Outputs
| Name | Type |
|---|---|
| **HasChanged** | System.Boolean |
| **Delta** | System.Single |
| **DeltaOnHit** | System.Single |

