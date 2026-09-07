# PointsOnImage

*in [Lib.point.generate](README.md)*

Uses the image brightness to emit points.

WARNING: this is extremely slow for high resolution. We recommend setting fixed resolutions like 512x512px.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **IsEnabled** (Boolean) | In certain situations you can save resources by disabling update.<br/>E.g. you could use [Once] to only compute a single distribution if Image and Seed do not change. |
| **Count** (Int32) | The count of emitted points. Note that the resolution of the connected image has a larger impact on performance than the number of points. |
| **GainAndBias** (Vector2) | Applies bias and gain before sampling the image. This can be useful to increase the contrast of the distribution. |
| **ScatterWithinPixel** (Single) | On images with very small resolution the pixels can become noticeable. This parameter scatters the points within the pixels.<br/>Values larger than one add additional scattering. |
| **ApplyColorToPoints** (Boolean) | Samples the image color (including alpha) to the Point attribute. If disabled, points will be white. |
| **Seed** (Int32) | A random seed. Consider connecting [CountInt] to add animated dithering. |
| **ColorWeight** (Vector4) | — |
| **ClampThreshold** (Single) | — |
| **Mode** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **OutputPoints** | T3.Core.DataTypes.BufferWithViews |

