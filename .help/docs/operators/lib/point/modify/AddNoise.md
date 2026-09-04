# AddNoise

*in [Lib.point.modify](README.md)*

Creates a new buffer by resampling the connected points. This can be useful for increasing resolution or smoothing out hard edges.

Also see [SimNoiseOffset] and [TurbulenceForce]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Strength** (Single) | Defines the total intensity of the random movements. |
| **StrengthFactor** (Int32) | — |
| **Frequency** (Single) | Defines the frequency, i.e., how fast the points move. |
| **Phase** (Single) | Defines the phase of the noise and can be animated with a [Value], e.g., using [Time]. |
| **Variation** (Single) | Adds more variance and detail to the noise. |
| **AmountDistribution** (Vector3) | Multiplier for the individual axes to amplify/attenuate the noise in certain directions. |
| **RotationLookupDistance** (Single) | Defines the rotation lookup distance |
| **NoiseOffset** (Vector3) | Shifts the noise per axis. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

