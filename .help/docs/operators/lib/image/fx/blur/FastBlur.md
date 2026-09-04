# FastBlur

*in [Lib.image.fx.blur](README.md)*

Provides better quality and much faster speed that the [Blur] but does only allow radius control.

This effect uses the Marius Bjørge / Masaki Kawase dual-filtering method. It provides a cinematic, high-quality light scatter by recursively downsampling and then upsampling the image using a 9-tap tent kernel. It is optimized for mobile and VR-level performance while providing 'Gaussian-level' smoothness.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **MaxLevels** (Int32) | The number of blur levels applied. The maximum is 12 (which should be enough for most resolutions).<br/><br/>In most scenarios you wouldn't adjust this, but in edge scenarios, it might help to optimize performance or craft special looks. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Texture2D |

