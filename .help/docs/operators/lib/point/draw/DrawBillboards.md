# DrawBillboards

*in [Lib.point.draw](README.md)*

Draws points and billboards or quads. This operator is very flexible and allows for a wide spectrum of effects.

Its parameters are grouped into different sections for various aspects of variations.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | Input for GPoints |
| **Scale** (Single) | The overall scale of the drawn sprites. |
| **Stretch** (Vector2) | Non-uniformly scales the billboards<br/>X = Width<br/>Y = Length |
| **UseWForScale** (Boolean) | Defines whether the "W"-attribute of the Points used has an influence on the size of the rendered Billboards |
| **UsePointScale** (Boolean) | If enabled, the points' stretch attribute will be applied to the drawn billboards. |
| **Offset** (Vector3) | Moves the Billboards' local point space. |
| **Orientation** (Int32) | Defines how a billboard is aligned towards the camera:<br/><br/>If using point rotation, the orientation to the camera will be ignored. |
| **RotateZ** (Single) | Rotates a billboard around its own axis without affecting the Orientation. |
| **RotationAxis** (Vector3) | Defines how strongly the rotation influences the individual axis. |
| **Randomize** (Single) | Multiplier for controlling the following random values |
| **RandomPhase** (Single) | Phase value for changing the randomness. Can be animated with [Time], for example.<br/>Note that this random seed is continuous and can be used for animations. |
| **RandomPosition** (Vector3) | Amount of random offset along X, Y and Z axis. |
| **RandomRotate** (Single) | Multiplier for random rotation |
| **RandomScale** (Single) | Multiplier for random uniform scaling |
| **Color** (Vector4) | Defines the color of the Billboards.<br/>If a Texture is used this color will be multiplied. |
| **ColorVariationMode** (Int32) | Defines how colors from the gradient will be sampled: <br/><br/>- RandomWithPhase will pick a random color. This will smoothly animate when changing RandomPhase.<br/>- Scatter will pick a random color. Each point gets a random position that will NOT change.<br/>- Spread will use the point's ordering in the buffer. 0 for the first point and 1 for the last. This spreading can be adjusted with the Spread options below.<br/>- W uses the "w" attribute of the points. This can be useful when combined with a [ParticleSystem] that returns particle age in the W attribute.<br/>- FogDistance can be combined with a [SetFog] operator to colorize points depending on their distance to the camera. |
| **ColorVariations** (Gradient) | — |
| **ScaleDistribution** (Int32) | Defines how the scale curve below will be sampled in the range between 0 .. 1. <br/><br/>- RandomWithPhase will pick a random scale position. This will smoothly animate when changing RandomPhase.<br/>- Scatter will pick a random scale from the curve. Each point gets a random scale that will NOT change.<br/>- Spread will use the point's ordering in the buffer. 0 for the first point and 1 for the last. This spreading can be adjusted with the Spread options below.<br/>- W uses the "w" attribute of the points.<br/>- FogDistance can be combined with a [SetFog] operator to scale points depending on their distance to the camera. |
| **Scales** (Curve) | Scaling of the curve. Use the icon in the top right corner to edit this curve in a popup window.<br/> |
| **SpreadLength** (Single) | If using the Spread mode in one of the parameters above, this will change the width of the slice that settings (e.g. Colors) are applied to.  |
| **SpreadPhase** (Single) | Can offset the spreading range. This can be useful for "sliding" through a gradient of colors or sizes. |
| **SpreadPingPong** (Boolean) | If enabled, the gradients and curves are mirrored when spread mode is active. |
| **SpreadRepeat** (Boolean) | If enabled, will repeat the spreading slice. This can be especially useful when combined with smaller Spread length and Spread Offset. |
| **Texture_** (Texture2D) | — |
| **AtlasMode** (Int32) | — |
| **AtlasSize** (Int2) | Defines the number of cells in the connect texture atlas. This can be useful for distributing different random sprites with a single draw call. |
| **FxTexture** (Texture2D) | The FX texture is an additional method of styling the sprites. It's applied in screen space. <br/>As an example: You could connect a [Blob] image to fade out the sprites at the edges of the screen. |
| **FxTextureMode** (Int32) | Switches how the FX-Texture is used. |
| **FxTextureAmount** (Vector4) | — |
| **BlendMode** (Int32) | Selects the Blendmode |
| **EnableDepthWrite** (Boolean) | Defines whether Billboards cover themselves or are covered by or cover other 3D elements. |
| **AlphaCut** (Single) | Bias value that can be used to influence how partially transparent pixels behave. It can be used to fine-tune the boundary to full transparency if an alpha mask is used |
| **TooCloseFadeOut** (Boolean) | Points too close to the camera will fade out |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

