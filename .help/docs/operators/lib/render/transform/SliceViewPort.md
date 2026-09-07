# SliceViewPort

*in [Lib.render.transform](README.md)*

Modifies the Viewport and projection matrix to help drawing grid cells. 

In the simplest form this can be used to limit the rendering to a letterbox format (i.e., with black bars on top and bottom).
In a more complex setup it can be combined with Loop to draw a grid of render passes. See example for more details.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SubGraph** (Command Required) | — |
| **CellIndex** (Int32 Required) | — |
| **CellCounts** (Int2) | — |
| **Stretch** (Vector2) | — |
| **Mode** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
| **Count** | System.Int32 |

