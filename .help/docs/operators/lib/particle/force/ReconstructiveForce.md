# ReconstructiveForce

*in [Lib.particle.force](README.md)*

Blends the simulation particle points toward the original matching emit points.

Note: The effect is likely to produce unpredictable results if the number of emit points does not match the number of simulation points.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **TargetPoints** (BufferWithViews Required) | — |
| **Strength** (Single) | — |
| **DistanceMode** (Int32) | — |
| **VolumeCenter** (Vector3) | — |
| **VolumeStretch** (Vector3) | — |
| **VolumeScale** (Single) | — |
| **VolumeRotate** (Vector3) | — |
| **FallOff** (Single) | — |
| **Bias** (Single) | — |
| **GizmoVisibility** (GizmoVisibility) | — |

## Outputs
| Name | Type |
|---|---|
| **ParticleSystem** | T3.Core.DataTypes.ParticleSystem |

