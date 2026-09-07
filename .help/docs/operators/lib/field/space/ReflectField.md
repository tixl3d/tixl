# ReflectField

*in [Lib.field.space](README.md)*

Folds / kinks / reflects the incoming field through a plane.
It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

Similar nodes: [BendField], [PolarRepeat], [TwistField].

It needs an incoming field like [BoxSDF], [ChainLinkSDF], [FractalSDF] as an input.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode) | — |
| **PlaneNormal** (Vector3) | Defines the rotation of the plane which reflects the incoming field |
| **Offset** (Single) | Moves / transforms the position of the reflection plane |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

