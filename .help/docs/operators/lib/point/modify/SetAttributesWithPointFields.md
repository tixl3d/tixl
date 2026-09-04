# SetAttributesWithPointFields

*in [Lib.point.modify](README.md)*

Sets various attribute points from the distance of a 2nd (small) set of points.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Amount** (Single) | — |
| **Range** (Single) | — |
| **OffsetRange** (Single) | — |
| **BiasAndGain** (Vector2) | — |
| **Variation** (Single) | — |
| **AffectColor** (Single) | — |
| **ColorMode** (Int32) | — |
| **Gradient** (Gradient) | — |
| **AffectPosition** (Single) | — |
| **AffectOrientation** (Single) | — |
| **OrientationUpVector** (Vector3) | — |
| **AffectW** (Single) | — |
| **WMode** (Int32) | — |
| **WCurveAffectsWeight** (Boolean) | — |
| **WCurve** (Curve) | — |
| **Points** (BufferWithViews Required) | — |
| **FieldPoints** (BufferWithViews Required) | Controls the modification field. Note that count is clamped to 10. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

