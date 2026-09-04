# PerlinNoise3

*in [Lib.numbers.vec3](README.md)*

Generates a continuously and randomly changing value in the predefined range based on Perlin Noise/Gradient Noise.

Useful to create random-looking animations e.g. flickering light, wind, fire, cam shake, changing colors etc.

This creates PerlinNoise in 3 dimensions. See [PerlinNoise] and [PerlinNoise2] for operators with fewer dimensions

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **OverrideTime** (Single) | Input for a value to override the time |
| **Phase** (Single) | — |
| **Frequency** (Single) | Defines the frequency. A high frequency generates more variation per time unit. |
| **Octaves** (Int32) | Defines the number of octaves or the level of detail and variation of the noise over time. |
| **AmplitudeXYZ** (Vector3) | Multiplier for the intensity of the highest and lowest value split for each value |
| **Amplitude** (Single) | Multiplier for the intensity of the highest and lowest value. |
| **Offset** (Vector3) | — |
| **RangeMin** (Vector3) | Defines the lowest value of the noise. |
| **RangeMax** (Vector3) | Defines the highest value of the noise. |
| **BiasAndGain** (Vector2) | Defines bias and gain, which can be used to weight the ranges (both in terms of time and intensity) in which the values are most likely to fluctuate |
| **Seed** (Int32) | Defines the seed for the randomness. Two exactly identical copies of the operator with different seeds always create different results. |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Numerics.Vector3 |

