# DataPointConverter

*in [Lib.point.io](README.md)*

Converts point data from CSV or JSON files into a GPU structured buffer of Point objects. It supports custom column mapping for CSV files and can export the converted points back to CSV or JSON.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Convert** (Boolean) | A trigger to start the conversion process. This reads the input file and generates the point buffer based on the column mappings below. |
| **CsvF1Mapping** (String) | The name of the column/property for a custom floating-point attribute (F1). |
| **CsvPosXMapping** (String) | The name of the column/property for the point's X position. |
| **CsvPosYMapping** (String) | The name of the column/property for the point's Y position. |
| **CsvPosZMapping** (String) | The name of the column/property for the point's Z position. |
| **CsvRotWMapping** (String) | The name of the column/property for the point's W rotation (Quaternion component). If not provided, rotation is treated as Euler angles (Yaw, Pitch, Roll). |
| **CsvRotXMapping** (String) | The name of the column/property for the point's X rotation (Euler angle or Quaternion component). |
| **CsvRotYMapping** (String) | The name of the column/property for the point's Y rotation (Euler angle or Quaternion component). |
| **CsvRotZMapping** (String) | The name of the column/property for the point's Z rotation (Euler angle or Quaternion component). |
| **CsvScaleXMapping** (String) | The name of the column/property for the point's X scale. |
| **CsvScaleYMapping** (String) | The name of the column/property for the point's Y scale. |
| **CsvScaleZMapping** (String) | The name of the column/property for the point's Z scale. |
| **Export** (Boolean) | A trigger to save the generated point buffer to the specified ExportFilePath. |
| **ExportFilePath** (String) | The path where the converted point data will be saved when you trigger the export. Supports .csv and .json. |
| **FilePath** (String) | The path to the input CSV or JSON file containing the point data. |

## Outputs
| Name | Type |
|---|---|
| **PointBuffer** | T3.Core.DataTypes.BufferWithViews |

