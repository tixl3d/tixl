# MakeTileableImageAdvanced

*in [Lib.image.transform](README.md)*

Makes an incoming image tileable based on linear edge falloff combined with a tweakable noise pattern that can also be animated.
Useful to avoid visible repetitive patterns that can happen with the similar and simpler [MakeTileableImage].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ImageA** (Texture2D) | — |
| **EdgeFallOff** (Single) | Defines the size / length of the transition |
| **TilingMode** (Int32) | Defines how the image is tiled:<br/><br/>0 = No Tiling<br/>1 = horizontal tiling only<br/>2 = vertical tiling only<br/>3 = vertical and horizontal tiling |
| **AddNoiseToTransition** (Boolean) | Enables / disables a noise pattern in the transition |
| **FadeOut** (Single) | Defines the subtlety / how intense the noise pattern is shown in the tiling. |
| **Scale** (Single) | Defines the size of the noise pattern |
| **Phase** (Single) | Can be used to randomly change the noise pattern.<br/>Can also be animated (for example with [Time]...) |

## Outputs
| Name | Type |
|---|---|
| **Selected** | T3.Core.DataTypes.Texture2D |

