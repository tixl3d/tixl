# ChainLinkSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a procedural chain link field which can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].
Can be used with [RepeatFieldLimit] to generate a chain.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [BoxSDF], [CapsuleLineSDF], [FractalSDF].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | Transforms the center of the object |
| **Length** (Single) | Defines the length |
| **Size** (Single) | Defines the radius / size |
| **Thickness** (Single) | Defines the thickness |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

