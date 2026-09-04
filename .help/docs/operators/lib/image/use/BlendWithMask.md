# BlendWithMask

*in [Lib.image.use](README.md)*

Blends two images by the brightness of a 3rd mask image.

In the mask, fully black areas will only render that portion of image A, and fully white areas will only render that of image B. Values in between will interpolate between the two.

All blend ops and similar: [BlendImages] [Blend] [BlendWithMask] [Combine3Images]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ImageA** (Texture2D Required) | — |
| **ColorA** (Vector4) | — |
| **ImageB** (Texture2D Required) | — |
| **ColorB** (Vector4) | — |
| **Mask** (Texture2D Required) | — |
| **Resolution** (Int2) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

