# Bloom

*in [Lib.image.fx.blur](README.md)*

A more versatile and faster version of [Glow].

This uses a down sampling and blurring cascade to create multiple blurred copies of the original image and then combines these layers additively while applying a gradient.

Please also try the presets and check the documentation of the parameters.

Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAbberation] [Bloom] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Intensity** (Single) | The overall intensity values below 1 are hardly noticeable. |
| **ColorWeights** (Vector4) | Colors to calculate the initial luminance used for the glow. This can be useful for limiting the effect on certain colors.<br/>The default values are the NTSC perception of color channels.<br/><br/>Reducing the alpha channel blends these colors to gray. |
| **Threshold** (Single) | Limit the glow to brighter areas. |
| **GlowGradient** (Gradient) | Can be used to colorize or shape the glow. <br/>It's multiplied onto each blur kernel with the more blurred levels on the right.<br/><br/>TIP:<br/>- You can also adjust the brightness above 1 (hold CTRL while dragging the brightness slider) to amplify levels like the core. |
| **GainAndBias** (Vector2) | This can be used to shape the distribution of the blur kernels<br/><br/>Lower curves focus on the core, higher curves on the blurred parts.<br/><br/>Many settings cause artifacts, but when used subtly can be very useful for crafting a look. |
| **MaxLevels** (Int32) | The number of blur levels applied. The maximum is 12 (which should be enough for most resolutions).<br/><br/>In most scenarios you wouldn't adjust this, but in edge scenarios, it might help to optimize performance or craft special looks. |
| **Blur** (Single) | Offsets the blur amount. This might be useful to craft looks (e.g. to limit the glow spread).<br/><br/>But it will cause noticeable artifacts. |
| **Clamp** (Boolean) | Clamp the blur kernels before combining. <br/>This will give a slightly different look. It will _NOT_ clamp the results. Use [ToneMap] for that. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Texture2D |

