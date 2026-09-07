# RemapFloatList

*in [Lib.numbers.floats.process](README.md)*

Remaps a list of float values from an input range to an output range.

This operator is useful for scaling or normalizing lists of numbers. For example, you can use it to map sensor readings from their raw range to a 0-1 range for use in animations.

Tips:
- Use the `Mode` to control how the remapping is applied (e.g., clamping, looping).
- `BiasAndGain` provides an alternative way to adjust the mapping curve.

AKA: scale, normalize, range, map

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **BiasAndGain** (Vector2) | An alternative control for remapping. Bias shifts the center of the range, while Gain controls the steepness of the mapping curve. |
| **FloatList** (List`1) | The list of float values to be remapped. |
| **Mode** (Int32) | Specifies how values outside the input range are handled. Options include clamping, looping, and mirroring. |
| **RangeInMax** (Single) | The maximum value of the input range. |
| **RangeInMin** (Single) | The minimum value of the input range. |
| **RangeOutMax** (Single) | The maximum value of the output range. |
| **RangeOutMin** (Single) | The minimum value of the output range. |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Collections.Generic.List`1[System.Single] |

