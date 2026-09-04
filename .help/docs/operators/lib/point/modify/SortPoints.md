# SortPoints

*in [Lib.point.modify](README.md)*

Sort points by distance to camera, so that the distant sprites can be drawn first, and ones closer to camera get blended correctly

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **CameraReference** (Object Required) | — |
| **SortingSpeed** (Single) | — |
| **Ascending** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |
| **DebugView** | T3.Core.DataTypes.Texture2D |

