# TwistField

*in [Lib.field.space](README.md)*

Twists the input field along the given axis.
This works best for small source volumes within unit range.

It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Other nodes: [RepeatWithMirror], [PolarRepeat], [ReflectField].

It needs an incoming field like [BoxSDF], [SphereSDF], [TorusSDF] as an input.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].




## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode Required) | — |
| **Amount** (Single) | Defines the amount of twisting |
| **Axis** (Int32) | Defines the axis around which the field is to be warped |
| **StepFactor** (Single) | Decrease step size to avoid raymarching artifacts. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

