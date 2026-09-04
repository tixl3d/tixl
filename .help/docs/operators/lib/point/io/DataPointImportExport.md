# DataPointImportExport

*in [Lib.point.io](README.md)*

Imports a JSON point list into a GPU structured buffer and optionally exports it again. The node keeps the imported data in an internal buffer, so the points remain visible even when the import trigger is released. It also features an autoload capability that re-imports the file when the ImportFilePath is modified.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **PointBufferIn** (BufferWithViews) | An optional incoming point buffer. If a buffer is connected, the Export trigger will use this data. If not connected, Export will use the last successfully imported data. |
| **ImportFilePath** (String) | The path to the JSON file to import. The operator will automatically try to load the file if this path changes. |
| **ExportFilePath** (String) | The path where the JSON file will be saved when Export is triggered. |
| **Import** (Boolean) | A trigger to load point data from the specified ImportFilePath. The loaded data is stored internally and exposed via PointBufferOut. |
| **Export** (Boolean) | A trigger to save point data to the ExportFilePath. It exports from PointBufferIn if connected; otherwise, it exports the last imported data. |

## Outputs
| Name | Type |
|---|---|
| **PointBufferOut** | T3.Core.DataTypes.BufferWithViews |

