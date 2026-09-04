# DrawMeshHatched

*in [Lib.mesh.draw](README.md)*

Draws incoming geometry and meshnodes in an abstract shading style.

An interactive tutorial for the complete TiXL render pipeline can be found at [HowToDrawThings].

The most commonly used render methods are [Drawmesh], [DrawMeshUnlit], [DrawMeshHatched], and [DrawMeshAtPoints].

They can then be combined with [SetMaterial], [SetFog], [SetPointLight], and many others to create the look of scenes.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ColorHighlight** (Vector4) | Defines the color in which the bright areas / lines are displayed. |
| **ColorShade** (Vector4) | Defines the color in which the dark / shaded areas are displayed |
| **LineWidth** (Single) | Thickness of the Lines / Highlighted areas |
| **FollowSurface** (Single) | Defines how strongly the lines follow the surface of the mesh.<br/><br/>Low values: The lines mainly follow the angle of the view.<br/>High values: Lines also increasingly flow along the surfaces of the mesh |
| **OffsetDirection** (Single) | Rotates the alignment of the lines. |
| **RandomFaceDirection** (Single) | Randomly rotates all lines on all faces. |
| **RandomFaceLighting** (Single) | Brightens random faces to varying degrees. |
| **Shading** (Gradient) | Defines the gradient between highlight and shade colors |
| **Culling** (CullMode) | Defines the transparency of the surfaces.<br/>None: All surfaces are "bothsided" or always visible from all sides<br/>Front: The normals are flipped (frontside is transparent / backside visible)<br/>Back: Default (Frontside is visible / backside is invisible) |
| **EnableZTest** (Boolean) | This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **EnableZWrite** (Boolean) | This defines whether the mesh covers itself or is covered by or covers other meshes. |
| **ColorMap** (Texture2D) | Texture Input from [LoadImage]. |
| **Mesh** (MeshBuffers Required) | Input for Incoming Mesh Geometry |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

