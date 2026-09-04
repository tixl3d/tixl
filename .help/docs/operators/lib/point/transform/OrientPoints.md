# OrientPoints

*in [Lib.point.transform](README.md)*

Orients the rotation of points so that Z points towards a target position. You can also orient the point to the screen or the camera.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Amount** (Single) | — |
| **AmountFactor** (Int32) | — |
| **Target** (Vector3) | Target Position |
| **UpVector** (Vector3) | — |
| **Flip** (Boolean) | — |
| **OrientationMode** (Int32) | — |
| **WIsWeight** (Boolean) | Deprecated. Please use AmountFactor |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

