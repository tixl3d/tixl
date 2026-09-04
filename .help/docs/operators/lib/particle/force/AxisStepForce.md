# AxisStepForce

*in [Lib.particle.force](README.md)*

A force for the [ParticleSystem] that applies random accelerations toward an axis direction. This can lead to interesting results in motion design. If ApplyTrigger is true, a random selection ratio of particles is chosen and accelerated.

Use the following options:

- AxisDistribution to control the frequency of XYZ axis selection.
- AddOriginalVelocity to control whether the original acceleration is replaced or mixed in.
- Set Seed to a value greater than 0 to control randomness. If it's set to 0, the current frame number is used as the random seed.

Please note that the RotationAxis space may depend on the [ParticleSystem.OrientTowardsVelocity] parameter. You can also try combining this effect with [ParticleSystem.Damp] for enhanced results.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ApplyTrigger** (Boolean) | — |
| **Strength** (Single) | — |
| **RandomizeStrength** (Single) | — |
| **SelectRatio** (Single) | — |
| **AxisDistribution** (Vector3) | — |
| **AddOriginalVelocity** (Single) | — |
| **StrengthDistribution** (Vector3) | — |
| **AxisSpace** (Int32) | — |
| **Seed** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Particles** | T3.Core.DataTypes.ParticleSystem |

