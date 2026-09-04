# Translate

*in [Lib.field.space](README.md)*

Transforms the position of the field in 3D space.
Similar node with more options: [TransformField]

It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Other nodes: [RepeatWithMirror], [PolarRepeat], [TwistField].

It needs an incoming field like [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode) | Field input |
| **Translation** (Vector3) | Defines the offset of the field in space |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

