# RepeatFieldLimit

*in [Lib.field.space](README.md)*

Repeats the incoming field in the defined direction.
Similar nodes: [RepeatField3], [RepeatWithMirror]

It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Other nodes: [PolarRepeat], [TwistField].

It needs an incoming field like [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode) | — |
| **Axis** (Int32) | Defines along which axis the field is to be repeated |
| **Size** (Single) | Defines the size of the gaps between the repetitions |
| **Start** (Single) | Defines how often the field is to be repeated in one direction along the axis |
| **Stop** (Single) | Defines how often the field is to be repeated in the other direction along the axis |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

