# GrowStrains

*in [Lib.point.sim.experimental](README.md)*

Uses CollectedSpawnPoints and lines to animate W
as weights moving across the strain with the age of 
the spawn point.

We use a look-up texture that defines the growth 
progression as follows:

- Age from left (birth) to right (death)
- Strain-Length from bottom (root / spawn position) to top.
- R - Width
- G - Noise amount
- B - Twist


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews) | — |
| **GTargets** (BufferWithViews) | — |
| **Variation** (Single) | — |
| **NoiseAmount** (Single) | — |
| **Frequency** (Single) | — |
| **NoisePhase** (Single) | — |
| **NoiseDistribution** (Vector3) | — |
| **NoiseRotationLookUp** (Single) | — |
| **Length** (Single) | — |
| **Width** (Single) | — |
| **NoiseDensity** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

