# ReorientLinePoints

*in [Lib.point.transform](README.md)*

Align the orientation of points so that z- poitns forward.
Try to use the current point orientation to avoid relying on up-vector discontinuities.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Amount** (Single) | — |
| **Center** (Vector3) | — |
| **UpVector** (Vector3) | — |
| **WIsWeight** (Boolean) | — |
| **Flip** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

