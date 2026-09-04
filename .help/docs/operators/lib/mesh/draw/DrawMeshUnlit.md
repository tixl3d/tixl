# DrawMeshUnlit

*in [Lib.mesh.draw](README.md)*

Draws incoming geometry and meshnodes without any shading and according to the desired settings.

An interactive tutorial for the complete TiXL render pipeline can be found at [HowToDrawThings].

The most commonly used render methods are [Drawmesh], [DrawMeshUnlit], [DrawMeshHatched], and [DrawMeshAtPoints].

They can then be combined with [SetMaterial], [SetFog], [SetPointLight], and many others to create the look of scenes.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | Input for Incoming Mesh Geometry |
| **Color** (Vector4) | Defines the shading color of the mesh.<br/>If a [SetMaterial] is used it will be ignored. |
| **BlendMode** (Int32) | Selects the Blendmode. |
| **FillMode** (Int32) | — |
| **Culling** (CullMode) | Defines the transparency of the surfaces.<br/>None: All surfaces are "bothsided" or always visible from all sides<br/>Front: The normals are flipped (frontside is transparent / backside visible)<br/>Back: Default (Frontside is visible / backside is invisible) |
| **EnableZTest** (Boolean) | This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **EnableZWrite** (Boolean) | This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **Texture** (Texture2D Relevant) | Texture Input from [LoadImage]. |
| **UseCubeMap** (Boolean) | Toggles whether a Cubemap with [SetEnvironment] is used. |
| **AlphaCutOff** (Single) | This value controls transparency if a texture containing an alpha channel is used. |
| **BlurLevel** (Single) | Defines how intensely the used texture is blurred. |
| **TextureWrap** (TextureAddressMode) | — |
| **UseVertexColor** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

