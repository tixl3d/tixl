# TurbulenceForce

*in [Lib.particle.force](README.md)*

Adds a turbulence force to a Particle Simulation.

Also see [SimNoiseOffset] and [AddNoise]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Amount** (Single) | Defines the total intensity of the random movements. |
| **Frequency** (Single) | Defines the frequency, i.e. how fast the points move. |
| **Phase** (Single) | Defines the phase of the noise and can be animated with a [Value], e.g. using [Time]. |
| **Variation** (Single) | Adds more variance and detail to the noise. |
| **ValueField** (ShaderGraphNode) | — |
| **VariationGroupCount** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Particles** | T3.Core.DataTypes.ParticleSystem |

