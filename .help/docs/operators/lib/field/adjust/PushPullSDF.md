# PushPullSDF

*in [Lib.field.adjust](README.md)*

Makes the incoming SDF volumes thicker or thinner by pushing or pulling the surface by adding a constant value to the distance.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SdfField** (ShaderGraphNode Required) | — |
| **AmountField** (ShaderGraphNode) | Add additional scale field to simulate some kind of displacement.<br/>This will break the Lipschitz continuity of your field and cause artifacts when raymarching. |
| **Amount** (Single) | — |
| **StepScale** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

