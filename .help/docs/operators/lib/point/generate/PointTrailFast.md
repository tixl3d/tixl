# PointTrailFast

*in [Lib.point.generate](README.md)*

Keeps previous copies of points in a cycling buffer that can be used to draw trails and other effects.
Hint: If some features don't work as expected try [PointTrail2]

Can be used with [DrawPoints] [DrawLines] [DrawBillboards] and similar

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | Point input |
| **TrailLength** (Int32) | Defines the length of the trail by the amount of points added |
| **IsEnabled** (Boolean) | Enables / disables the trail |
| **Reset** (Boolean) | Resets the trail when clicked |
| **AddSeperatorThreshold** (Single) | Defines the threshold for the separator |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

