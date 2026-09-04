# CombineMaterialChannels2

*in [Lib.image.use](README.md)*

Combines roughness, metallic, and ambient occlusion texture maps into a single texture for [SetMaterial].

If no normal map is available [NormalMap] can be used to generate one.

Newer version: [CombineMaterialChannels2]

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

