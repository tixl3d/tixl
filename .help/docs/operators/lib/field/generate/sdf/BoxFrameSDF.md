# BoxFrameSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a procedural Box Frame field which can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].
Can be used with [RepeatFieldLimit] to generate a chain.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [BoxSDF], [OctahedronSDF], [PyramidSDF].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | Position in space |
| **Size** (Vector3) | Defines the size of the box<br/>x = width<br/>y = height<br/>z = depth |
| **UniformScale** (Single) | Scales uniformly in all directions |
| **Thickness** (Single) | Defines how thick is the frame |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

