# RaymarchPoints

*in [Lib.field.use](README.md)*

Moves points along their Z-axis until they hit an SDF surface.

This can be used for visualizing raymarching or simlurate rays being reflected from an SDF surface.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Field** (ShaderGraphNode Required) | — |
| **Points** (BufferWithViews Required) | — |
| **MaxSteps** (Int32) | — |
| **MinDistance** (Single) | — |
| **StepDistanceFactor** (Single) | — |
| **MaxReflectionCount** (Int32) | — |
| **Mode** (Int32) | — |
| **WriteDistanceTo** (Int32) | — |
| **WriteStepCountTo** (Int32) | — |
| **NormalSamplingDistance** (Single) | — |
| **MaxDistance** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result2** | T3.Core.DataTypes.BufferWithViews |

