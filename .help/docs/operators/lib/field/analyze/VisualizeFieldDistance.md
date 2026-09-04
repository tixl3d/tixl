# VisualizeFieldDistance

*in [Lib.field.analyze](README.md)*

Visualizes the distance a field by drawing a plane with contour lines.

This plane can be rotated or moved through your field. This can be very useful to analyze and understand your field and inspect the Lipschitz continuity and potential problems for [RaymarchSDF].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SdfField** (ShaderGraphNode Required) | — |
| **Center** (Vector3) | — |
| **Rotation** (Vector3) | — |
| **Size** (Vector2) | — |
| **LineColor** (Vector4) | — |
| **Background** (Vector4) | — |
| **EnableZTest** (Boolean) | — |
| **EnableZWrite** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **DrawCommand** | T3.Core.DataTypes.Command |

