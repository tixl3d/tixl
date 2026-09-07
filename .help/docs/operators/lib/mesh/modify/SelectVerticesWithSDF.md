# SelectVerticesWithSDF

*in [Lib.mesh.modify](README.md)*

Allows selecting and adding values to vertices in 3D space via SDF Objects/Fields.
Similar to [SelectPoints].
Needs any SDF such as [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input to select the points.
And any form of points such as: [GridPoints], [RadialPoints], [SpherePoints], [PointsOnMesh], etc.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | — |
| **SdfField** (ShaderGraphNode Required) | — |
| **Strength** (Single) | — |
| **StrengthFactor** (Int32) | — |
| **WriteTo** (Int32) | — |
| **Mode** (Int32) | — |
| **Mapping** (Int32) | — |
| **Range** (Single) | — |
| **Offset** (Single) | — |
| **GainAndBias** (Vector2) | — |
| **Scatter** (Single) | — |
| **ClampNegative** (Boolean) | Prevent the result from becoming negative (which could lead to points with inverted scale). |
| **DiscardNonSelected** (Boolean) | Converts points with zero or below selection to Separators (Scale = NaN) so they will not be rendered. |

## Outputs
| Name | Type |
|---|---|
| **ResultMesh** | T3.Core.DataTypes.MeshBuffers |

