# PlaneSDF

*in [Lib.field.generate.sdf](README.md)*

Creates a flat surface as an SDF field.
Generates a procedural flat surface field which can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

For a simple and interactive tutorial on the TiXL rendering pipeline for signed distance fields, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [BoxSDF], [ChainLinkSDF], [FractalSDF].

To use a Mesh made of polygons refer to [QuadMesh].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | — |
| **Axis** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

