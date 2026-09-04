# PointSimulation

*in [Lib.point.sim](README.md)*

Creates a simulation buffer for applying simulation-like force fields, curl noise, or flocking simulation. 

It initially creates a copy of the connected source point buffer. You can then reset to this initial state or constantly blend towards this original state. This can be very helpful to "mix in" a valid state and thus keep a simulation under control.

Although the operators in the namespace lib.point.sim are optimized to apply modifications onto this "simulated" buffer, the [ParticleSystem] provides a more convenient setup for this.

Also see: [SimulateBoidsExample], [HowToUsePoints].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | — |
| **MixOriginal** (Single) | Controls how much of the original state is being restored:<br/><br/>- 0: The simulation runs completely free.<br/>- 0.03: A small percentage is constantly mixed into the simulation, thus avoiding too extreme deviations.<br/>- 1: Equivalent to resetting to the source.<br/><br/>For experimental effects, you can use mix values exceeding the range between 0 and 1. |
| **Reset** (Boolean) | Completely restores the buffer with the input buffer. This is equivalent to setting MixRate to 1.<br/> |
| **Update** (Boolean) | Restore input buffer with the MixRate. |
| **MinCapacity** (Int32) | If the size of the input buffer changes, the simulation will start from scratch.<br/>Setting this optional initial size will only reset the simulation if the new input buffer exceeds the reserved size. |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

