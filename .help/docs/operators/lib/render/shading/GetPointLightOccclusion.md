# GetPointLightOccclusion

*in [Lib.render.shading](README.md)*

Returns a float list with the visibility of the current point lights.

This means: This operator can recognize if a point light is being occluded by another object. It can be used to control the intensity of effects like [LenseFlare], glow, godrays etc.

Use the operator called [UseRenderTarget] to get a reference to the current render target and connect its Depth output.
The output value can then be connected to [LenseFlareSetup] Brightness parameter.

[UseRenderTarget] -> [GetPointLightOcclusion] -> [LenseFlareSetup]



## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **DepthMap** (Texture2D Required) | — |
| **NearFarRange** (Vector2) | — |
| **LightIndex** (Int32) | — |
| **Damping** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Occlusion** | System.Single |
| **Output** | T3.Core.DataTypes.Texture2D |

