# SetEnvironment

*in [Lib.render.shading](README.md)*

Sets the image-based lighting (IBL) for the current RenderTarget. This texture can then be used by drawing operators for physically based rendering (PBR) further left in the graph.
The operators needed are: [LoadImage] -> [TextureToCubeMap] -> [SetEnvironment]


Some background information: a standard technology for rendering is to use environment textures that store various degrees of roughness in their mip map levels: fully reflective chrome in the highest resolution and very blurry/diffuse reflections in the lower levels. This is not just done by blurring, though: a single ultra bright pixel (e.g. the sun) can brighten the complete diffuse reflection. This computation is very(!) expensive and should not be done in real-time for high resolution environment maps.

To compute this IBL map [SetEnvironment] needs a cube map texture. As of now you can load equirectangular HDR maps only in DDS format and convert them with a [TextureToCubemap]. We are working on making this more convenient. You can also use procedural image textures for this.

NOTICE: For the background environment to have the correct alignment, the camera must be set after the environment:

[DrawMesh]->[SetMaterial]->[SetPointLight]->[SetEnvironment]->[SetCamera].

Also see: [HowToDrawThings]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SubTree** (Command Required) | — |
| **Texture** (Texture2D Required) | Cubemap input to overwrite the default cubemap.<br/>This can be used with the [TextureToCubeMap] operator. |
| **UpdateLive** (Boolean) | Refreshes any changes to the image and exposure settings. This can be kept turned on but will significantly decrease rendering performance.<br/><br/>If turned on this allows animated "Exposure" values and live changes in the incoming image. |
| **Exposure** (Single) | Defines the intensity with which the scene is illuminated by the image.<br/><br/>If changing this value has no effect temporarily turn on and off the "UpdateLive" setting.<br/> |
| **RenderBackground** (Boolean) | If turned on the environment will be visible to the camera by being mapped on a sphere mesh around the scene. |
| **BackgroundBlur** (Single) | Defines how much the image on the sphere in the background is blurred.<br/> |
| **BackgroundColor** (Vector4) | Tints the background with the selected color.<br/><br/>Does not affect the color of the light and/or reflection. |
| **BackgroundDistance** (Single) | Sets the radius for the sphere mesh that surrounds the scene.<br/><br/>ProTip: Clipping of the background can be prevented either by this setting or by the "NearFarClip" settings in the used camera. |
| **QualityFactor** (Single) | A factor that is applied to the sample count for the different roughness levels.<br/>When enabling Live update this has significant(!) impact on your rendering performance, so turn this down until the artifacts are noticable. |
| **Orientation** (Single) | — |
| **Fallback** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

