# SnapPointsToGrid

*in [Lib.point.transform](README.md)*

Rounds (floors) the position of points onto a grid, i.e., snaps them to the position of a grid.

This can be useful to debug compute shader effects that rely on a spatial grid.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Amount** (Single) | — |
| **AmountFactor** (Int32) | — |
| **Mode** (Int32) | — |
| **GridScale** (Single) | — |
| **GridStretch** (Vector3) | — |
| **Offset** (Vector3) | — |
| **Scatter** (Single) | — |
| **BiasAndGain** (Vector2) | — |
| **UseWAsWeight** (Boolean) | This is parameter has been deprecated and is only here for reference. |
| **UseSelection** (Boolean) | This is parameter has been deprecated and is only here for reference. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

