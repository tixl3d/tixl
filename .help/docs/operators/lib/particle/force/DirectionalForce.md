# DirectionalForce

*in [Lib.particle.force](README.md)*

Generates a force acting on particles in a defined direction in space.

Useful for simulating gravity, wind or attractive forces.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Amount** (Single) | Strength of the force |
| **Direction** (Vector3) | Defines the direction in which the force acts<br/><br/>ProTip: The 'ShowGizmo' feature creates a set of arrows that makes adjusting the direction easier |
| **RandomAmount** (Single) | Adds or subtracts a random value per particle |
| **ShowGizmo** (GizmoVisibility) | Defines the visibility of the gizmo |

## Outputs
| Name | Type |
|---|---|
| **Particles** | T3.Core.DataTypes.ParticleSystem |

