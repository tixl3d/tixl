# MoveToSDF

*in [Lib.point.modify](README.md)*

Moves points to the nearest SDF surface.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | The original points. |
| **Field** (ShaderGraphNode Required) | The SDF field providing the distance. |
| **Amount** (Single) | The factor how much the of the effect is applied.<br/>This can also be negative to move points away from the surface. |
| **MaxSteps** (Int32) | The maximum number of steps for marching the field.<br/>The max is clamped internally to 100. |
| **MinDistance** (Single) | The distance at which the raymarching is stopped. |
| **NormalSamplingDistance** (Single) | We have to rely on the field's gradient to estimate step direction.<br/>Depending on the provided field, increasing this distance "blurs" the field and thus helps with local inconsistencies. |
| **StepDistanceFactor** (Single) | We have to rely on the field's gradient to estimate the marching directions.<br/>This factor is applied on the distance field steps and can help with fields that don't have a constant Lipschitz property. |
| **WriteDistanceMode** (Int32) | Allows to store the distance to the SDF in one of the FX attributes. This might be useful in combinations with [RepeatAtPoints]. |
| **SetOrientation** (Boolean) | Apply the SDF normal direction as new point orientation.<br/>This uses an up vector and might be prone to instabilities. |
| **AmountFactor** (Int32) | — |
| **SetColor** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Result2** | T3.Core.DataTypes.BufferWithViews |

