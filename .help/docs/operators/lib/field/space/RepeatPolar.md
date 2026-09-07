# RepeatPolar

*in [Lib.field.space](README.md)*

Mirrors and rotates the incoming field in the desired number around a central axis.

It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Similar nodes: [ReflectField], [RepeatWithMirror], [TwistField].

It needs an incoming field like [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode) | — |
| **Axis** (Int32) | Defines the axis along which the field is aligned<br/>(If changing this value has no effect, any other value must be changed to bring the change into effect) |
| **Repetitions** (Single) | Defines how often the incoming object is repeated |
| **Offset** (Single) | Rotates the field around its own axis |
| **Mirror** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

