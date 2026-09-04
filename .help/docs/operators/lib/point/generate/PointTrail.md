# PointTrail

*in [Lib.point.generate](README.md)*

Same as [PointTrail] with added internal features for [DrawBillboards]
Keeps previous copies of points in a cycling buffer that can be used to draw trails and other effects.

Can be used with [DrawPoints] [DrawLines] [DrawBillboards] and similar

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | Point input |
| **TrailLength** (Int32) | Defines the length of the trail by the amount of points added |
| **WriteTrailOrderTo** (Int32) | — |
| **IsEnabled** (Boolean) | Enables / disables the trail |
| **Reset** (Boolean) | Resets the trail when clicked |
| **WriteLineSeparators** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

