# SdfReflectionLinePoints

*in [Lib.field.use](README.md)*

Moves points along their Z-axis until they hit an SDF surface.

This can be used for visualizing raymarching or simlurate rays being reflected from an SDF surface.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Field** (ShaderGraphNode Required) | — |
| **MaxReflectionCount** (Int32) | — |
| **MaxSteps** (Int32) | — |
| **MinDistance** (Single) | — |
| **StepDistanceFactor** (Single) | — |
| **WriteDistanceTo** (Int32) | — |
| **WriteStepCountTo** (Int32) | — |
| **NormalSamplingDistance** (Single) | — |
| **MaxDistance** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result2** | T3.Core.DataTypes.BufferWithViews |

