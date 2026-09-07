# DrawMeshAtPoints

*in [Lib.mesh.draw](README.md)*

Draws PBR shaded instances of a mesh defined by the connected point buffer. 

It uses the current settings for material, point lights, fog, and environment. It can use the point's F1 and F2 attribute to control the size of the instances.

Check out the [HowToDrawThings] on how to set these up.

Also check out [DrawMesh] and [RepeatMeshAtPoints].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | Input for incoming Points which are used to spawn meshes. |
| **Mesh** (MeshBuffers Required) | Input for incoming Mesh Geometry |
| **Size** (Single) | Uniformly scales the meshes using the Points as center of scaling. |
| **ScaleFactor** (Int32) | — |
| **UsePointScale** (Boolean) | Defines whether the meshes are influenced/non-uniformly stretched by their points. |
| **Color** (Vector4) | Defines the shading color of the mesh.<br/>If a [SetMaterial] is used, these colors are multiplied with its settings. |
| **AlphaCutOff** (Single) | This value controls transparency if a texture containing an alpha channel is used. |
| **BlendMode** (Int32) | Selects the Blendmode. |
| **FillMode** (Int32) | Toggles between colored Wireframe Rendering or default shading method. |
| **CullMode** (CullMode) | Defines the transparency of the surfaces.<br/><br/>None: All surfaces are "bothsided" or always visible from all sides<br/><br/>Front: The normals are flipped (frontside is transparent / backside visible)<br/><br/>Back: Default (frontside is visible / backside is invisible) |
| **Shading** (Int32) | Select shading mode |
| **SpecularAA** (Single) | This reduces specular aliasing on silhouettes and high-frequency normalmap regions. |
| **EnableZWrite** (Boolean) | This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **EnableZTest** (Boolean) | This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **ColorField** (ShaderGraphNode) | — |
| **AtlasSize** (Int2) | Defines the number of cells in the connect texture atlas. This can be useful for distributing different textures with a single draw call. |
| **AtlasMode** (Int32) | Defines how the atlas textures are spread.  |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

