# SimNoiseOffset

*in [Lib.point.sim](README.md)*

Adds Perlin curl noise to a point buffer. 

Note: Different from [AddNoise], this effect modifies the original buffer and is therefore not deterministic.
Also see [TurbulenceForce]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | — |
| **Amount** (Single) | Defines the total intensity of the random movements. |
| **Frequency** (Single) | Defines the frequency, i.e. how fast the points move. |
| **Phase** (Single) | Defines the phase of the noise and can be animated with a [Value], e.g. using [Time]. |
| **Variation** (Single) | Adds more variance and detail to the noise. |
| **AmountDistribution** (Vector3) | Multiplier for the individual axes to amplify / attenuate the noise in certain directions. |
| **RotLookupDistance** (Single) | Defines the rotation lookup distance |
| **UseCurlNoise** (Boolean) | Switches back and forth between different noise variants |
| **IsEnabled** (Boolean) | Toggles the effect on / off |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

