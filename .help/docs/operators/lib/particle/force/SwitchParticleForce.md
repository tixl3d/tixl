# SwitchParticleForce

*in [Lib.particle.force](README.md)*

Switches between connected forces for the ParticleSystem. Can be used to switch between forces or deactivate all.
The index starts with 0 for the first input and will wrap on values exceeding the count of connected inputs.

Tip: 
- use index -1 to activate none (i.e. disable all)
- use index -2 to activate all

Is used with [ParticleSystem] and any force such as [AxisStepForce], [TurbulenceForce] etc.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Input** (ParticleSystem Required) | — |
| **Index** (Int32) | Defines which of the incoming Forces is active<br/>-1 disables all forces<br/>-2 activates all forces |

## Outputs
| Name | Type |
|---|---|
| **Selected** | T3.Core.DataTypes.ParticleSystem |

