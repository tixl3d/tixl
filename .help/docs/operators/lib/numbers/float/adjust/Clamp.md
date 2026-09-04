# Clamp

*in [Lib.numbers.float.adjust](README.md)*

Clamps an input float between two values.
Can be used to find minimum or maximum values.

AKA: Min, Max

Tips: Also consider using [Remap] with clamping enabled.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Value** (Single) | Input value to clamp. Will be output if it falls between the Min and Max values. |
| **Min** (Single) | Lower bound. If Value is below this, the operator will output this instead. |
| **Max** (Single) | Upper bound. If Value is above this, the operator will output this instead. |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |

