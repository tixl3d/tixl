# CombineMaterialChannels

*in [Lib.image.use](README.md)*

Combines roughness, metallic, and ambient occlusion texture maps that are loaded with [LoadImage] into a single texture for [SetMaterial] to create PBR materials.

If no normal map is available, [NormalMap] can be used to generate one.

Older version: [CombineMaterialChannels2]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Roughness** (Texture2D Required) | Input for a RoughnessMap via a [LoadImage] operator. |
| **Metallic** (Texture2D Required) | Input for a MetallicMap via a [LoadImage] operator. |
| **Occlusion** (Texture2D Relevant) | Input for an AmbientOcclusionMap via a [LoadImage] operator. |
| **Resolution** (Int2) | Overwrite the resolution of the input image. |
| **GenerateMips** (Boolean) | Enables the generation of mipmaps which ensure that textures also look good from different distances and angles. |
| **RemapRoughness** (Curve) | Hold the "Alt" key and left-click to create points on the curve to fine-tune how the loaded roughness map is used.<br/>Right-click on the points opens more options such as interpolation etc. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

