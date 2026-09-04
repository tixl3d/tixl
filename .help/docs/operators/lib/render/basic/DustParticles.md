# DustParticles

*in [Lib.render.basic](README.md)*

Creates a volume around the camera in which points that behave like dust or snow are spawned and repeated / wrapped infinitely.

Tip: This effect can be combined with [SetFog], [DepthOfField], [MotionBlur] among others.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Refresh** (Boolean) | If no particles are visible double-click to reset the particle simulation<br/><br/>This can become necessary if a change in parameters such as 'count' or 'WrapSize' results in no particles being displayed |
| **Count** (Int32) | Controls the number of particles<br/>Changing this setting may require a refresh |
| **WrapSize** (Single) | Changes the size of the volume around the camera in which particles are spawned and repeated<br/>Changing this setting may require a refresh |
| **OffsetVolume** (Vector3) | Offsets the volume that spawns and wraps the particles from the position of the camera |
| **Color** (Vector4) | Defines the color of the particles |
| **Size** (Single) | Defines the size of the particles |
| **Gravity** (Vector3) | Adds a linear force in the defined axis |
| **NoiseAmount** (Single) | Changes the speed of the noise animation |
| **NoiseFrequency** (Single) | Changes the granularity of the noise<br/>Higher values create many small spatially limited turbulences<br/>Lower values create movements that affect more particles at the same time |
| **NoiseVariation** (Single) | Adds a per-particle noise variation |
| **Phase** (Single) | Changes the phase / random seed of the noise animation |
| **TrailLength** (Int32) | Adds a tail of particles to each particle |
| **TrailColor** (Vector4) | Defines the color of the trails |
| **TrailWidth** (Single) | Changes the thickness of the trails |
| **FadeNearest** (Single) | Defines how close to the camera the particles start to fade out |
| **EnableZWrite** (Boolean) | Enables / disables Z-writing / sorting |
| **BlendMode** (Int32) | Defines what blend mode is used to render the particles |
| **Texture_** (Texture2D) | Input for a custom texture which overrides the default point / blob |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

