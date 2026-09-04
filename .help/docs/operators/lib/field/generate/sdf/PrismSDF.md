# PrismSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a prism SDF (i.e. an n-sided cylinder).

NOTE: the triangular prism does not support rounding.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | Transforms the center of the object |
| **Radius** (Single) | Defines the size |
| **Length** (Single) | Defines the height |
| **EdgeRadius** (Single) | Defines the smoothness of the edge |
| **Sides** (Int32) | Switches between a 3-sided and 6-sided Prism <br/>(If changing this value has no effect, any other value must be changed to bring the change into effect) |
| **Axis** (Int32) | Defines the axis along which the field is aligned |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

