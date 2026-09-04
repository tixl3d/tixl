# SetMaterial

*in [Lib.render.shading](README.md)*

Sets the Physically Based Rendering (PBR) Material for the current RenderTarget which is then used by [DrawMesh] and other PBR rendering operators. Each of the material properties can be controlled by a color and/or by connecting a texture input by using a [LoadImage] operator.

Please note that the color parameter is multiplied to the texture: so for an emissive texture to be visible, you have to first set the Emissive Color to white (or 1,1,1).

The base (albedo) color can also be adjusted by the Draw operators.
Please use [CombineMaterialChannels] to combine Roughness, Metallic and Occlusion textures into a single texture that can be connected to the .RoughnessMetallicOcclusion parameter.
For the roughness rendering the draw operators need an IBL texture set by [SetEnvironment]. If [SetEnvironment] is not used there is still a predefined default IBL.


Also see: [HowToDrawThings]

If no normalmap is available [NormalMap] can be used to generate one.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SubTree** (Command Required) | — |
| **BaseColor** (Vector4) | Defines the Color which is multiplied with the "BaseColorMap". |
| **BaseColorMap** (Texture2D) | Input for a Diffuse- / Albedo- / ColorMap or Texture via a [LoadImage] Operator. |
| **EmissiveColor** (Vector4) | Defines the color and brightness with which the material emits light*.<br/><br/>Please Note: If an [EmissiveColorMap] is used this must be set to white!<br/><br/>*Please Note: As of now: Materials in T3 cannot be used to *actually* illuminate scenes. Scenes are lit with a combination of [SetEnvironment] and [Pointlight] Operators. |
| **EmissiveColorMap** (Texture2D) | Input for an Emissive Color Map via a [LoadImage] Operator.<br/>Please Note: The "Emissive Color" must be set to white, otherwise the image will always be rendered black. |
| **Specular** (Single) | Defines how intense the highlights of light sources are visible. |
| **Roughness** (Single) | Defines how much reflections and highlights are blurred. |
| **Metal** (Single) | Defines how similar to metal the material reacts to light and reflects the environment.<br/><br/>A value of 1 can create a look similar to chrome. |
| **NormalMap** (Texture2D) | Input for a Normalmap via a [LoadImage] Operator. |
| **RoughnessMetallicOcclusionMap** (Texture2D) | — |
| **MaterialId** (String) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
| **Reference** | T3.Core.Rendering.Material.PbrMaterial |

