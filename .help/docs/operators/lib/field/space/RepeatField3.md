# RepeatField3

*in [Lib.field.space](README.md)*

Infinitely repeats the incoming field in every direction.
Similar node: [RepeatFieldLimit]
It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Other nodes: [RepeatWithMirror], [PolarRepeat], [TwistField].

It needs an incoming field like [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode) | — |
| **Size** (Vector3) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

