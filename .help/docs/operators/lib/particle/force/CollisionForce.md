# CollisionForce

*in [Lib.particle.force](README.md)*

A simple simulation of sphere collision between points. The radius of the points is defined on emit by the [ParticleSystem]'s PointRadiusW factor.

Some recommendations:
1. Try to increase the radius (either by increasing the emit points W attribute or the radius factor) and adjust the [DrawPoints] radius accordingly.
2. Try to increase [ParticleSystem] damping.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **CellSize** (Single) | — |
| **Bounciness** (Single) | — |
| **Attraction** (Single) | — |
| **IsEnabled** (Boolean) | — |
| **AttractionDecay** (Single) | — |
| **CollistionResolve** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Particles** | T3.Core.DataTypes.ParticleSystem |

