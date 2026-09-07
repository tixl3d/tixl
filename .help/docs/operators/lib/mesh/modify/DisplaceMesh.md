# DisplaceMesh

*in [Lib.mesh.modify](README.md)*

Distorts the input mesh by displacing its vertices by an amount controlled by the connected input texture.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputMesh** (MeshBuffers Required) | — |
| **Mode** (Int32) | — |
| **Amount** (Single) | — |
| **AmountDistribution** (Vector3) | Applies a channel weight to the axis. |
| **Offset** (Vector3) | Additional offsets to the displacement without applying the displacement map. |
| **Texture** (Texture2D Required) | The displacement texture. Depending on the color channels, it will be applied as average or individually.<br/>The alpha is always applied as a factor. |
| **UvSelection** (Int32) | — |
| **WrapMode** (Int32) | — |
| **ScaleUV** (Vector2) | Scales the displacement map. |
| **UseVertexSelection** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshBuffers |

