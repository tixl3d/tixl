# VectorFieldForce

*in [Lib.particle.force](README.md)*

Applies a vector field to the particle velocity. 

Note:  This will constantly pump more energy into your particle velocity. You might need to adjust the ParticleSystem Damp parameter.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **VectorField** (ShaderGraphNode Required) | — |
| **Amount** (Single) | — |
| **Randomize** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Particles** | T3.Core.DataTypes.ParticleSystem |

