# DrawScene

*in [Lib.render.scene](README.md)*

Draw the connected SceneSetup.

You can use [LoadGltfScene] to load a scene.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Scene** (SceneSetup Required) | Scene Input from [LoadGltfScene]-Op |
| **Color** (Vector4) | Defines the color and alpha with which the loaded scene is colored / changed. |
| **UseSceneMaterials** (Boolean) | If enabled Tool uses the materials from the SceneSetup. <br/>If disabled these materials are ignored and the material defined by [SetMaterial] in the context will be used. |
| **UseMaterialId** (String) | If Scene materials are disabled you can select a context material defined with [DefineMaterials]. |
| **BlendMode** (Int32) | Defines how the scene / geometry / materials are rendered. |
| **FillMode** (Int32) | Defines whether the surfaces are rendered normally or as wireframe (with colors of the materials). |
| **Culling** (CullMode) | Defines the transparency of the surfaces.<br/>None: All surfaces are "bothsided" or always visible from all sides<br/>Front: The normals are flipped (frontside is transparent / backside visible)<br/>Back: Default (Frontside is visible / backside is invisible) |
| **Shading** (Int32) | — |
| **SpecularAA** (Single) | This reduces specular aliasing on silhouettes and high-frequency normalmap regions. |
| **EnableZTest** (Boolean) | If enabled discards fragments sorted out by z-buffer.<br/><br/>This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **EnableZWrite** (Boolean) | This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **AlphaCutOff** (Single) | Defines the threshold value from which a transparent surface is rendered as such |
| **Filter** (Filter) | Defines which filter method is used to render the textures. |
| **WrapMode** (TextureAddressMode) | Defines how a texture is displayed at the edge when repeated. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

