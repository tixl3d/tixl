# RepeatAtPoints

*in [Lib.point.generate](README.md)*

Repeats a list of GPU points at positions provided by another list of points. The orientation of the target points can be applied, so this operator can be used to create point instantiation.

If the ApplyTargetScaleW parameter is activated, the W attribute of the target points is used as a scale factor.

Also known as: Copy, Replicate, Instance

Useful combinations: [CommonPointSets] [DrawLines] [DrawPoints]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GTargets** (BufferWithViews Required) | The incoming points define the position at which other points will be distributed |
| **GPoints** (BufferWithViews Required) | Defines what set of points is being distributed |
| **CombineMode** (Int32) | — |
| **Scale** (Single) | — |
| **ScaleFactor** (Int32) | — |
| **SetF1To** (Int32) | — |
| **SetF2To** (Int32) | — |
| **ApplyPointScale** (Boolean) | — |
| **ApplyOrientation** (Boolean) | — |
| **AddSeparators** (Boolean) | — |
| **ApplyTargetScaleW** (Boolean) | — |
| **MultiplyTargetW** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

