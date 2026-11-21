# RenderToCubeMap

*in [Lib.render.shading](README.md)*

Renders the connected scene command into a cube map texture.

The **Command** subtree is evaluated once per face with a 90° field of view, either from the cube's
center (**Position**/**Rotation**) or from a connected **CameraReference**, and the six faces are
copied into one `TextureCube`. Multisampling, FXAA and mip maps are supported. Use it for custom
environment maps, reflections or sky boxes; for image-based lighting from a 2D image use
[TextureToCubeMap](TextureToCubeMap.md).

Inside the scene the current face index is available as the `CubeFace` int variable.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command) | Scene subtree to render into the six faces. |
| **Resolution** (Int32) | Edge length of each face in pixels (4..4096). |
| **ClearColor** (Vector4) | Color the faces are cleared with before drawing. |
| **TextureFormat** (Format) | Format of the resulting cube map; unknown formats fall back to the default. |
| **GenerateMips** (Boolean) | Generate a mip chain for the cube map. |
| **Multisampling** (Int32) | MSAA sample count per face (1 = off). |
| **PostAntiAliasing** (Int32) | 0 = none, 1 = FXAA applied while copying the faces. |
| **Position** (Vector3) | Center of the cube when no camera reference is connected. |
| **Rotation** (Vector3) | Face orientation in degrees when no camera reference is connected. |
| **ClipPlanes** (Vector2) | Near and far planes; a connected camera's projection overrides them when recoverable. |
| **CameraReference** (Object) | Optional camera whose position, orientation and clip planes drive the six face views. |

## Outputs
| Name | Type |
|---|---|
| **CubeMap** | T3.Core.DataTypes.Texture2D |

**See also:** [RaymarchToCubeMap](../../field/render/RaymarchToCubeMap.md), [TextureToCubeMap](TextureToCubeMap.md), [SetEnvironment](SetEnvironment.md)

---
*Generated from operator definition in Lib.render.shading*
