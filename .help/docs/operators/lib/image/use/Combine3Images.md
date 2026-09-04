# Combine3Images

*in [Lib.image.use](README.md)*

A node to combine 3 input images into the RGBA channels of a new one.

All blend ops and similar: [BlendImages] [Blend] [BlendWithMask] [Combine3Images]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ImageA** (Texture2D Required) | — |
| **ColorA** (Vector4) | — |
| **ImageB** (Texture2D Required) | — |
| **ColorB** (Vector4) | — |
| **ImageC** (Texture2D Required) | — |
| **ColorC** (Vector4) | — |
| **SelectChannel_R** (Int32) | — |
| **SelectChannel_G** (Int32) | — |
| **SelectChannel_B** (Int32) | — |
| **SelectAlphaChannel** (Int32) | — |
| **GenerateMips** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

