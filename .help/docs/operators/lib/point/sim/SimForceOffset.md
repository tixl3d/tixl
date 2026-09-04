# SimForceOffset

*in [Lib.point.sim](README.md)*

Creates a gizmo with a spherical force that can be used to affect points

Needs a [DrawPoints], [DrawLines], or [DrawMeshAtPoints] or similar in order to become visible.

Needs Points as a base, for example: [RadialPoints], [SpherePoints], [PointsOnMesh]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews) | Point input |
| **Center** (Vector3) | Defines the position of the Force |
| **Radius** (Single) | Defines the size of the Force |
| **RadiusFallOff** (Single) | Defines the size of the falloff / smooth area |
| **RadialForce** (Single) | Multiplier to increase the amount of force |
| **UseWForMass** (Single) | Controls how much the F-Value is affected |
| **Variation** (Single) | Randomizes the amount of the force |
| **Gravity** (Vector3) | Adds a linear force on the defined axis |
| **ForceDecayRate** (Single) | Defines how much the force decays based on the distance |
| **IsEnabled** (Boolean) | Toggles the Force off / on |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

