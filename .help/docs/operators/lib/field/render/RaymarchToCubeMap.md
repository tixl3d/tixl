# RaymarchToCubeMap

*in [Lib.field.render](README.md)*

Renders an SDF field into a cube map texture by raymarching the field into each of the six faces.

Connect any SDF field (e.g. [MeshToSDF](../generate/sdf/MeshToSDF.md), [SphereSDF], [CombineSDF])
to **SdfField**. The output is a `TextureCube` that can be used for environment maps, reflections or
[custom shaders], with optional mip maps and an equi-angular cubemap (EAC) layout.

The raymarch parameters mirror [RaymarchField](RaymarchField.md). **Position** and **Rotation**
place the field in the cube's local space; **Resolution** is the edge length of each face (the
default 512 is already expensive - 64..256 is usually enough for lighting).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SdfField** (ShaderGraphNode Required) | The signed distance field to raymarch. |
| **Color** (Vector4) | A color multiplier for the field. |
| **AmbientOcclusion** (Vector4) | Ambient-occlusion color; the alpha channel controls its intensity. |
| **AoDistance** (Single) | Distance and intensity of the ambient occlusion. |
| **TextureScale** (Single) | Scales the material texture. |
| **UVMapping** (Int32) | UV mode: 0 global triplanar, 1 local triplanar, 2 local XY, 3 local YZ, 4 local XZ. |
| **NormalSamplingD** (Single) | Look-up distance for the surface normal. Too small values cause artifacts; larger values smooth the surface. |
| **MaxSteps** (Single) | Number of raymarching steps (typically 50-150). Impacts quality and performance. |
| **MinDistance** (Single) | Distance at which a hit is accepted. |
| **MaxDistance** (Single) | Distance at which the ray is abandoned. |
| **StepSize** (Single) | Initial step scaling; lower it to reduce artifacts from invalid Lipschitz continuities (e.g. noise). |
| **DistToColor** (Single) | Slight edge-darkening factor for compositing the field into a scene. |
| **SpecularAA** (Single) | Anti-aliasing amount for specular highlights. |
| **Position** (Vector3) | Position of the field in the cube's local space. |
| **Rotation** (Vector3) | Rotation of the field in degrees. |
| **Resolution** (Int32) | Edge length of each cube face in pixels (4..4096, default 512). |
| **ClearColor** (Vector4) | Color written where no surface is hit. |
| **GenerateMips** (Boolean) | Generate a mip chain for the cube map. |
| **Material** (PbrMaterial) | Optional material override for shading the field. |
| **EquiAngular** (Boolean) | Write the faces in equi-angular cubemap (EAC) layout. |

## Outputs
| Name | Type |
|---|---|
| **CubeMap** | T3.Core.DataTypes.Texture2D |
| **ShaderCode** | System.String |

**See also:** [MeshToSDF](../generate/sdf/MeshToSDF.md), [RaymarchField](RaymarchField.md), [RenderToCubeMap](../../render/shading/RenderToCubeMap.md)

---
*Generated from operator definition in Lib.field.render*
