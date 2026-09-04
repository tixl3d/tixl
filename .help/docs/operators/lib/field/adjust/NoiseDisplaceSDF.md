# NoiseDisplaceSDF

*in [Lib.field.adjust](README.md)*

Displaces the distance of an SDF with a Perlin-like noise offset.

NOTE: this operator will break the Lipschitz continuity of your field and will cause artifacts when raymarching.
It's sometimes possible to reduce some of these artifacts by reducing the step size for raymarching.

Please check the Additional documentation in [RaymarchSdf]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputField** (ShaderGraphNode Required) | — |
| **Amount** (Single) | Amount of the displacement. |
| **Scale** (Single) | The scale of the noise field. |
| **Offset** (Vector3) | Offset to the noise field. Try to animate this with [AnimVec3]. |
| **StepFactor** (Single) | Reduce step size to avoid raymarching artifacts. |
| **UseLocalSpace** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

