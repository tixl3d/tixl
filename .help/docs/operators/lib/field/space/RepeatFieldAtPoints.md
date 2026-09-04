# RepeatFieldAtPoints

*in [Lib.field.space](README.md)*

Repeats the incoming field at the connected points.
Warning: This operator is extremely slow especially when used with [RayMarchField].
Therefore the maximum point count is currently clamped at 50 to avoid GPU stalls.

It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Similar nodes: [RepeatWithMirror], [PolarRepeat].

It needs an incoming field like [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input for the field.
And it needs a [Point], [Radialpoints], [SpherePoints] or similar to define where the field is to be repeated.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode Required) | SDF input |
| **Points** (BufferWithViews Required) | Points input |
| **K** (Single) | Value that changes the transitions between the field objects depending on the Combine Method |
| **CombineMethod** (Int32) | Defines if and how the repeated field objects behave when they intersect |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

