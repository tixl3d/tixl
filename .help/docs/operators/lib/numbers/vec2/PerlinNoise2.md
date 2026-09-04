# PerlinNoise2

*in [Lib.numbers.vec2](README.md)*

Generates a continuously and randomly changing value in the predefined range based on Perlin Noise / Gradient Noise.

Useful to create random-looking animations e.g. flickering light, wind, fire, camshake, changing colors etc.

This creates PerlinNoise in 2 dimensions. See [PerlinNoise] and [PerlinNoise3] for Operators with less / more dimensions

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **OverrideTime** (Single) | Input for a value to override the time |
| **Phase** (Single) | — |
| **Frequency** (Single) | Defines the frequency. A high frequency generates more variation per time unit. |
| **Octaves** (Int32) | Defines the number of octaves or the level of detail and variation of the noise over time. |
| **AmplitudeXY** (Vector2) | Multiplier for the intensity of the highest and lowest value split for each value |
| **Offset** (Vector2) | — |
| **Amplitude** (Single) | Multiplier for the intensity of the highest and lowest value. |
| **RangeMin** (Vector2) | Defines the lowest value of the noise. |
| **RangeMax** (Vector2) | Defines the highest value of the noise. |
| **BiasAndGain** (Vector2) | Defines bias and gain, which can be used to weight the ranges (both in terms of time and intensity) in which the values are most likely to fluctuate |
| **Seed** (Int32) | Defines the seed for the randomness. Two exactly identical copies of the operator with different seeds always create different results. |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Numerics.Vector2 |

