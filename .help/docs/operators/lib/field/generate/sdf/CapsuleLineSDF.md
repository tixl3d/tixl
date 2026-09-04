# CapsuleLineSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a procedural capsule field by connecting two points.
It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [CapsuleSDF], [ChainLinkSDF], [FractalSDF].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | Transforms the center of the object |
| **StartingPoint** (Vector3) | Transforms the center of the starting point |
| **EndPoint** (Vector3) | Transforms the center of the end point |
| **Thickness** (Single) | Defines the thickness of the capsule |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

