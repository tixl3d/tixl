# Grain

*in [Lib.image.generate.noise](README.md)*

Adds animated image pixel noise similar to that of an analog TV or film grain.

ProTip: While this effect can be used in real-time applications at virtually no cost, it can have a significant negative impact on the effectiveness of image and video compression methods.

- This means that video streams and video files can lose quality and require more bandwidth or memory.
- Individual videos and image sequences can require considerably more storage space.

Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAberration] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **Amount** (Single) | Controls the intensity of the image noise. |
| **Color** (Single) | Controls the saturation of the noise.<br/><br/>0: monochromatic noise<br/>5: Colored Noise as in old TV Sets |
| **Exponent** (Single) | Controls the Exponent |
| **Brightness** (Single) | Controls the image brightness |
| **Animate** (Single) | Controls the speed of the animation<br/><br/>Low values can look like noise in liquids<br/><br/>High values (50 and up) can look like TV static or film grain |
| **RandomPhase** (Single) | — |
| **Scale** (Single) | Changes the size of the generated noise pattern / pixels. |
| **Resolution** (Int2) | Can be used to override the default render size. |
| **GenerateMipmaps** (Boolean) | Enables generation of mipmaps.<br/><br/>ProTip: This can be useful and produces better results if "Grain" is not used as a pure post effect, but is applied to a surface which is rendered in 3D space from different distances and angles. For example, a television / CCTV screen during a fly-through. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

