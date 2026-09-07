# Blend

*in [Lib.image.use](README.md)*

Blends two images.

If you want to cross fade (mix) images, consider using [BlendImages]

All blend ops and similar: [BlendImages] [Blend] [BlendWithMask] [Combine3Images]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ImageA** (Texture2D Required) | The background image defining the resolution of the output. |
| **ColorA** (Vector4) | A color multiplied onto the background image |
| **ImageB** (Texture2D Required) | The image blended on top of the background image. |
| **ColorB** (Vector4) | An optional color multiplied. The alpha channel can be used to fade out the image.<br/><br/>Consider connecting this to a [SampleGradient]. |
| **BlendMode** (Int32) | Various blending modes for the colors. |
| **AlphaMode** (Int32) | Various modes for how the alpha channels are combined. |
| **NormalForUpperHalf** (Boolean) | If used with blend modes other than normal, that blend mode will only be used if the alpha channel is below 0.5.<br/>Above that, the image will be fully blended with normal mode.<br/><br/>As an example: If blended with Screen mode...<br/>- Alpha = 0.0: none of the image will be visible<br/>- Alpha = 0.5: the image will be fully visible with screen mode<br/>- Alpha = 1.0: the image will be fully visible with normal mode<br/><br/> |
| **ScaleMode** (Int32) | — |
| **GenerateMips** (Boolean) | Generated MipMap levels. Please read the "Realtime Rendering for Artists" wiki page for mode details. |
| **Resolution** (Int2) | The target resolution. Please make sure to check the documentation on how T3 handles resolutions. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

