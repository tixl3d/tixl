# DrawMesh

*in [Lib.mesh.draw](README.md)*

Uses PBR rendering to draw incoming geometry and meshnodes according to the desired settings.
For convenience Tooll adds a default reflection and two point lights attached to the camera to a RenderTarget. You can override these by adding [SetEnvironment] and [SetMaterial] operators further up (further right) in your graph.
You can adjust various parameters to achieve wireframe or both sided rendering.

An interactive tutorial for the complete TiXL render pipeline can be found at [HowToDrawThings].

The most commonly used render methods are [DrawMesh], [DrawMeshUnlit], [DrawMeshHatched] and [DrawMeshAtPoints].

They can then be combined with [SetMaterial], [SetFog], [SetPointLight] and many others to create the look of scenes.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | Input for Incoming Mesh Geometry |
| **Color** (Vector4) | Defines the shading color of the mesh.<br/>If a [SetMaterial] is used, these colors are multiplied with its settings. |
| **AlphaCutOff** (Single) | This value controls transparency if a texture containing an alpha channel is used. |
| **BlendMode** (Int32) | Selects the Blendmode. |
| **FillMode** (Int32) | Toggles between colored Wireframe Rendering or default shading method. |
| **Culling** (CullMode) | Defines the transparency of the surfaces.<br/>None: All surfaces are "bothsided" or always visible from all sides<br/>Front: The normals are flipped (frontside is transparent / backside visible)<br/>Back: Default (Frontside is visible / backside is invisible) |
| **Shading** (Int32) | Select shading mode |
| **SpecularAA** (Single) | This reduces specular aliasing on silhouettes and high-frequency normalmap regions. |
| **EnableZTest** (Boolean) | If enabled discards fragments sorted out by z-buffer.<br/><br/>This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **EnableZWrite** (Boolean) | This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **Filter** (Filter) | Defines the mode for texture filtering |
| **WrapMode** (TextureAddressMode) | Defines how the texture behaves when repeated.<br/><br/>Wrap: The texture repeats itself continuously<br/>Mirror: The texture is mirrored and repeats infinitely.<br/>Clamp: The texture is cut off at the edge<br/>Border: Unclear<br/>MirrOnce: The texture is mirrored once, then cut off |
| **UseMaterialId** (String) | — |
| **FragmentField** (ShaderGraphNode) | An optional shader graph with a color function. <br/><br/>It will use the world position.xyz to generate a color that is multiplied before drawing the fragment.<br/>Try [SphereField]->[SdfToColor]->.FragmentField. |
| **ShaderDefines** (String) | Additional shader code that can be injected before rendering. <br/>Using this requirese knowledge about the internal implementation. Passing invalid code will break the rendering.<br/>This is similar to shader flags but most powerful because it also allows to define methods that are applied before rendering the fragment.<br/><br/>Options are...<br/><br/>#define USE_WORLDSPACE |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

