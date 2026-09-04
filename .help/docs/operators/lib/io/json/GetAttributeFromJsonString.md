# GetAttributeFromJsonString

*in [Lib.io.json](README.md)*

Loads a JSON string from a URL and outputs the defined part as a string.

Simpler alternative [RequestUrl]

Also see [LoadImageFromUrl] [WriteToFile] [ReadFile] [PickStringPart] [FilesInFolder]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **JsonString** (String) | Defines the URL / path to the JSON string |
| **ColumnName** (String) | Defines the column from which the string should be output |
| **RowIndex** (Int32) | Defines the row index (starting with 0) from which the string should be output |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.String |
| **Columns** | System.Collections.Generic.List`1[System.String] |
| **RowCount** | System.Int32 |

