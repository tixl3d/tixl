# Compare

*in [Lib.numbers.float.logic](README.md)*

Compares two float values, outputting a boolean.

AKA: greater, less, equal

Tips:
- If you want to output the min or max value of a set of floats, try [Clamp].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Value** (Single) | Input value. |
| **TestValue** (Single) | Value to compare input to. |
| **Mode** (Int32) | Which comparison to use.<br/>eg. in IsLarger mode, this op will output True if Value is greater than TestValue. |
| **Precision** (Single) | How close the inputs need to be for them to be considered equal.<br/> |

## Outputs
| Name | Type |
|---|---|
| **IsTrue** | System.Boolean |

