# GodRays

*in [Lib.render.postfx](README.md)*

Uses the z-buffer to draw god rays.

A different approach [LightRaysFx]

Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAberration] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | Image Input <br/>usually [Camera] -> [RenderTarget] |
| **DepthBuffer** (Texture2D Required) | DepthBuffer Input<br/>e.g. from [RenderTarget] |
| **CameraReference** (Object Required) | [Reference] from [Camera] Output |
| **LightPosition** (Vector3 Required) | Center / Position of the Light source<br/>ProTip: Use the position of a [Locator] for a [Pointlight] and as an Input |
| **RayColor** (Vector4) | Selects a color that is multiplied with the rays |
| **OriginalColor** (Vector4) | Tints / Multiplies a color with the original image input |
| **Samples** (Int32) | Controls the amount of samples.<br/>Higher values create smoother and brighter results at the price of performance |
| **CenterIntensity** (Single) | Defines the intensity of the effect |
| **RayIntensity** (Single) | Defines the brightness of the beams |
| **Decay** (Single) | Defines the degree to which the effect decreases with greater distance from the light source. |
| **ShiftDepth** (Single) | Defines the image depth up to which the effect should be applied |
| **Size** (Single) | Scales the samples to the depth of the image |
| **Offset** (Single) | Scales the image effect separately from the original image |
| **BlurSamples** (Int32) | Amount of Blur samples |
| **BlurSize** (Single) | Size of the blur effect |
| **BlurOffset** (Single) | Blur effect offset |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

