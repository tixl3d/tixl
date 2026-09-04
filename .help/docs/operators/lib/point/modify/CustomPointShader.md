# CustomPointShader

*in [Lib.point.modify](README.md)*

A very fast method of writing simple compute shaders to manipulate the connected points.

Each point has the following properties/attributes:

p.Position  // as float3
p.Rotation // as quaternion
p.FX1 // correspond to F1 for scaling and other effects.
p.FX2 // correspond to F2
p.Color // as float4
p.Scale // as float3

The following float values are initialized:

i - index of the point
f - a normalized float value of the index 0 ... 1.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Offset** (Vector3) | — |
| **A** (Single) | — |
| **B** (Single) | — |
| **C** (Single) | — |
| **D** (Single) | — |
| **GainAndBias** (Vector2) | — |
| **Image** (Texture2D) | — |
| **Gradient** (Gradient) | — |
| **ShaderCode** (String) | — |
| **AdditionalDefines** (String) | — |
| **TemplateFile** (String) | — |
| **Count** (Int32) | — |
| **ShaderResources** (ShaderResourceView) | — |
| **ConstantBuffers** (Buffer) | — |
| **TimeScale** (Single) | Warning if this is not enable to 0 the operator will no longer be cached. |
| **Field** (ShaderGraphNode) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |
| **ShaderCode_** | System.String |

