# PointsToCPU

*in [Lib.point.helper](README.md)*

Fetches a point list from GPU to CPU. This can be useful for later exporting to a file (e.g., with [ExportPointList]).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **PointBuffer** (BufferWithViews) | — |
| **TriggerUpdate** (Boolean) | — |
| **UpdateContinuously** (Boolean) | — |
| **StartIndex** (Int32) | Defines the start of the data slice read back from GPU. |
| **MaxCount** (Int32) | Reading down large chunks for data can critically slow down the user interface.<br/>For most diagnostic purposes you want to limit this count to a reasonable number. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.StructuredList |

