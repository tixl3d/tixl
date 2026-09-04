# ShadowPlane

*in [Lib.render.basic](README.md)*

Creates a Plane that is transparent except for a realtime rendered Shadowmap.

See [ShadowPlaneExample] on how to use it.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command) | Everything connected here will cast a shadow onto the ShadowPlane |
| **PlaneSize** (Single) | Can be used to scale the ShadowPlane if shadows are cut off too early |
| **FieldOfView** (Single) | This defines the angle at which the light casts a shadow. <br/>Since this operator treats any PointLight as a directional light, this can be helpful if lightsources are very close to the ShadowPlane and have to cast shadows in a wide 'Field of View'. |
| **Resolution** (Int32) | Sets the resolution of the 'ShadowMap' |
| **Color** (Vector4) | Defines the Color and Opacity of the shadow. |
| **BlurRadius** (Single) | Defines how strongly the shadows are blurred |
| **BlurDistribution** (Vector2) | Defines how strongly the shadows in the center (X) and at the edge (Y) of the ShadowPlane are blurred. |
| **Center** (Vector3) | Defines the Position of the ShadowPlane.<br/>The default is slightly above the ground.<br/> |
| **Rotation** (Vector3) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

