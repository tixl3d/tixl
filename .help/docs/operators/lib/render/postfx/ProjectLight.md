# ProjectLight

*in [Lib.render.postfx](README.md)*

Renders light "godrays" from a projected image into a scene casting nice shadows.

It's a post render effect rendering an image and a shadow pass and combining it while applying some colors parameters.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Scene** (Command Required) | — |
| **ViewCamReference** (Object Required) | — |
| **Image** (Texture2D Relevant) | — |
| **ProjectorType** (Int32) | — |
| **Position** (Vector3) | — |
| **Target** (Vector3) | — |
| **Scale** (Single) | — |
| **Roll** (Single) | — |
| **LightColor** (Vector4) | — |
| **RayIntensity** (Single) | — |
| **RaysDecay** (Single) | — |
| **StepCount** (Int32) | — |
| **AmbientColor** (Vector4) | — |
| **SurfaceIntensity** (Single) | — |
| **ShadowResolution** (Int32) | — |
| **ShadowBias** (Single) | — |
| **ShadowForm** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

