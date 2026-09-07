# RepeatAxis

*in [Lib.field.space](README.md)*

Infinitely mirrors and repeats the incoming field along the defined axis.
Similar node: [RepeatFieldLimit], [RepeatField3]

It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Other nodes: [RepeatWithMirror], [PolarRepeat], [TwistField].

It needs an incoming field like [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode) | — |
| **Size** (Single) | Defines the size of the gaps between the repetition |
| **Axis** (Int32) | Defines along which axis the field is to be repeated |
| **Mirror** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

