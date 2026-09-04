# FieldVolumeForce

*in [Lib.particle.force](README.md)*

Allows the use of a signed distance field as a force to manipulate a particle system.

Works as an input for [ParticleSystem].
Needs an SDF like [CylinderSDF], [SphereSDF] in order to work.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Field** (ShaderGraphNode Required) | — |
| **Amount** (Single) | — |
| **Attraction** (Single) | — |
| **AttractionDecay** (Single) | — |
| **Repulsion** (Single) | — |
| **ReflectOnCollision** (Boolean) | — |
| **Bounciness** (Single) | — |
| **RandomizeBounce** (Single) | — |
| **RandomizeReflection** (Single) | — |
| **InvertVolume** (Boolean) | — |
| **NormalSamplingDistance** (Single) | — |
| **ApplyColorOnCollision** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Particles** | T3.Core.DataTypes.ParticleSystem |

