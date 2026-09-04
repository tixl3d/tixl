# RaymarchField

*in [Lib.field.render](README.md)*

Renders the connected shader graph SDF.

It uses the following [SetMaterial], [SetFog] and [PointLight] override. 
It will correctly initialize the depth buffer so the output can be blended with other geometry like meshes.

Please check out the examples.

Also known as DrawField, DrawSDF, DrawRaymarchField, RaymarchSDF

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SdfField** (ShaderGraphNode Required) | — |
| **Color** (Vector4) | A color multiplier. You could also use [SetSDFMaterial] or [SetMaterial] for this. |
| **AmbientOcclusion** (Vector4) | Will apply an ambient occlusion color depending on the AO Distance parameter.<br/>You can use the alpha channel to control the intensity.<br/>Also try light colors to add some kind of inner glow effect. |
| **AoDistance** (Single) | Controls the distance and intensity of the Ambient Occlusion. |
| **TextureScale** (Single) | Scale texture defined by the [SetMaterial]. |
| **UVMapping** (Int32) | Provides various options to generate UVs for the field. |
| **NormalSamplingD** (Single) | Defines the look-up distance when calculating the surface normal. This controls how sharp objects appear.<br/>Too small values will cause artifacts. Larger values will apply some sort of smoothing effect. |
| **MaxSteps** (Single) | The number of raymarching steps. This has a major impact on the quality and performance. Normal values are between 50 and 150.<br/> |
| **MinDistance** (Single) | The distance threshold when the raymarching is marked as completed. |
| **MaxDistance** (Single) | A threshold after which the raymarching is aborted. |
| **StepSize** (Single) | An initial scaling factor. Lowering this can be useful to reduce artifacts caused by invalid Lipschitz continuities (e.g. by applying noise). |
| **DistToColor** (Single) | A minor factor that can help to reduce edge artifacts for compositing into the scene. |
| **WriteDepth** (Boolean) | — |
| **SpecularAA** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **DrawCommand** | T3.Core.DataTypes.Command |
| **ShaderCode** | System.String |

