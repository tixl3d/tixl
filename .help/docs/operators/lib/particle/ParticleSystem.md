# ParticleSystem

*in [Lib.particle](README.md)*

Emits particles on emit points and applies the connected forces.

Please check the how-to linked below [HowToUseParticles].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **EmitPoints** (BufferWithViews Required) | Input for GPoints |
| **Emit** (Boolean) | Enables emitting of the connected emit points. <br/><br/>Tip: For simulations of a fixed number of emitted points, you can set the MaxPointCount to -1 and use [Once] or [Trigger] to only trigger an initial emit burst. |
| **Reset** (Boolean) | Clears all emitted points.<br/><br/>Tip: You can connect [HasTimeChanged] to clean up on rewind or looping.  |
| **Update** (Boolean) | — |
| **MaxParticleCount** (Int32) | The length of the cycle buffer holding the simulated particles. Depending on the number of emitted particles it defines the maximum possible particle lifetime.<br/><br/>If set to -1 it will use the count of emit points. |
| **EmitVelocity** (Single) | An initial velocity that is set for emitted particles along their z-axis. |
| **EmitVelocityFactor** (Int32) | — |
| **RadiusFactor** (Single) | Some of the forces use the particle radius for collision calculation. The radius is set on emit time by the emit point Scale and this factor.<br/>For consistency we aligned that factor with the default point size for [DrawPoints] which renders points with a radius of 1/100 units. |
| **EmitMode** (Int32) | This will affect how particles are inserted into the particle buffer. This order will have an effect when drawing with lines.<br/>Note that switching this setting might require resetting the system. |
| **LifeTime** (Single) | The lifetime of particles in bars (depending on the current BPM rate).<br/>A negative setting will compute the maximum lifetime from the MaxParticleCount and the number of currently emitted particles.<br/>This will lead to flickering if changing the emit count during playback. |
| **Speed** (Single) | The simulation speed. If set to 0 the simulation will be paused.<br/>Negative simulation speed can shortly look like rewinding but the simulation will quickly become inconsistent. |
| **OrientTowardsVelocity** (Single) | Rotates the particles in relation to their velocity |
| **Drag** (Single) | A drag factor applied per simulation step. This can be useful for stabilizing simulations where forces insert too much energy (i.e., particle velocity) into the system. |
| **SetF1To** (Int32) | Can be used for customizing the rendering of the particles.<br/><br/>Note that many operators like [DrawPoints] use the W attribute to control the size of the points.<br/>With other operators like [DrawBillboards] you can use the W parameter to colorize particles over their lifetime.<br/><br/>Keep Original W -> no change<br/>Particle Age -> Normalized age between 0 ... 1<br/>Velocity -> A representation of the particle's velocity. |
| **SetF2To** (Int32) | — |
| **ParticleForces** (ParticleSystem Required) | Input for Forces or [SwitchParticleForce] |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

