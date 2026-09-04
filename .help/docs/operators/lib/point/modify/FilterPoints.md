# FilterPoints

*in [Lib.point.modify](README.md)*

Selects (i.e., picks) points based on the given criteria.

Can be used to reduce the overall amount of points to increase performance.

Useful combination [DrawPoints] [MeshFacesPoints] [MeshVerticesToPoints]

Vaguely similar [ClearSomePoints].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Count** (Int32) | Defines how many points are allowed through the filter |
| **StartIndex** (Int32) | Defines which point is defined as the first / starting point |
| **Step** (Single) | Defines the distance between the selected points.<br/>Or defines how many points are skipped during the selection if more than two points are passed through the filter. |
| **ScatterSelect** (Single) | Scatters the selection by adding randomness to the indexes |
| **Seed** (Int32) | Changes the random seed for the scatter effect |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

