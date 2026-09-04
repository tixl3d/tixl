# PeakLevel

*in [Lib.numbers.float.process](README.md)*

Analyzes the incoming value changes and outputs information about the peaks

Output:
1. attack level (float): Attack level
2. foundPeak (bool): Did the incoming value just reach a peak True / False
3. timeSincePeak (float): How much time has passed since the last peak
4. movingSum (float): Passing on the incoming value

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Value** (Single) | — |
| **Threshold** (Single) | Threshold value for the height of the peak from which it is recognized as a peak (e.g., to be able to filter out noise) |
| **MinTimeBetweenPeaks** (Single) | Defines the minimum amount of time that must have passed before a peak can be registered again |

## Outputs
| Name | Type |
|---|---|
| **AttackLevel** | System.Single |
| **FoundPeak** | System.Boolean |
| **TimeSincePeak** | System.Single |
| **MovingSum** | System.Single |

