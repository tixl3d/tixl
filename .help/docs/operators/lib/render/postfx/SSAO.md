# SSAO

*in [Lib.render.postfx](README.md)*

Adds a Screen Space Ambient Occlusion (SSAO) effect to a 3D rendered scene.

With this effect, a shading layer can be added that estimates how much a point or surface in the scene is exposed to ambient light.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **DepthBuffer** (Texture2D Required) | — |
| **NearFarRange** (Vector2) | Defines the range of the scene that the effect is applied to. |
| **NearFarClip** (Vector2) | Sets the Near and Far clipping. |
| **Color** (Vector4) | Selects the color which is applied to the parts of the image that are darkened. |
| **BoostShadows** (Vector2) | The first setting controls the bias shifting the whole effect.<br/>The second setting boosts the darker areas. |
| **Passes** (Single) | Controls the fidelity of the effect.<br/>Higher numbers create smoother results at the cost of rendering speed. |
| **Size** (Single) | Increases or decreases the size of the areas which are darkened. |
| **MixOriginal** (Single) | Controls how much the original image is mixed into the result. |
| **MultiplyOriginal** (Single) | Controls how much the original image is multiplied with the result. |
| **NoiseOffset** (Vector2) | Offsets the noise effect. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |
| **DepthBuffer2** | T3.Core.DataTypes.Texture2D |

