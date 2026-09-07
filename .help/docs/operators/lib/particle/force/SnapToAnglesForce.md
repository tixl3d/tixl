# SnapToAnglesForce

*in [Lib.particle.force](README.md)*

Slowly align particle velocity with repeated angle steps on the xy-plane. 

It works well when combined with [TurbulenceForce] and [PointTrail].

Note: This will have no effect if the particles have no default velocity or only along the z-axis.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Amount** (Single) | — |
| **AngleCount** (Single) | — |
| **VariationThreshold** (Single) | How frequently random rotations occur: 0 never. 1 always. |
| **Variation** (Single) | If a random threshold is exceeded, define the amount of random rotation the particle will do. |
| **KeepPlanar** (Single) | A factor that will keep the particle velocity planar to the camera.<br/>This might be useful to emphasize the angled direction if camera rotation changes. |
| **Twist** (Single) | A twist angle that is applied every frame. Might lead to unpredictable but interesting effects. |
| **Mode** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Particles** | T3.Core.DataTypes.ParticleSystem |

