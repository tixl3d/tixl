# TransformField

*in [Lib.field.space](README.md)*

Transforms, rotates and scales the incoming field in 3D space.
Similar node with fewer options: [Translate]

It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Other nodes: [RepeatWithMirror], [PolarRepeat], [TwistField].

It needs an incoming field like [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode Required) | — |
| **Translation** (Vector3) | Moves the incoming field<br/><br/>X (-left / +right), <br/>Y (-down / +up), <br/>Z (-forward / +backwards)<br/> |
| **Rotation** (Vector3) | Rotates the incoming field around the following axes:<br/><br/>X: Horizontal axis<br/>Y: Vertical axis<br/>Z: Forward axis<br/> |
| **Scale** (Vector3) | Scales the incoming field in the following directions:<br/><br/>X: Width<br/>Y: Height<br/>Z: Depth<br/> |
| **UniformScale** (Single) | Uniformly scales the incoming field |
| **Shear** (Vector3) | Shears the incoming field in the following directions:<br/><br/>X: Width<br/>Y: Height<br/>Z: Depth<br/> |
| **Pivot** (Vector3) | Moves the Pivot (center point) of the incoming field:<br/><br/>X (-left / +right) <br/>Y (-down / +up) <br/>Z (-forward / +backwards)<br/><br/>The Pivot Point determines the location of the incoming subgraph Gizmo. Transforming its location can make it easier to perform transformations around the position you want.<br/> |
| **RotateFieldVecs** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

