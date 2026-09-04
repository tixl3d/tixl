# BendField

*in [Lib.field.space](README.md)*

Bends / curves the incoming field along the given axis.
This works best for small source volumes within a limited unit range.
It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Similar nodes: [ReflectField], [RepeatWithMirror], [TwistField].

It needs an incoming field like [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode) | — |
| **Amount** (Single) | — |
| **Axis** (Int32) | Defines the axis along which the field is bent<br/>(If changing this value has no effect, any other value must be changed to bring the change into effect) |
| **StepFactor** (Single) | Reduce this factor to avoid ray marching artifacts. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

