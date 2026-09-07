# ReadFile

*in [Lib.io.file](README.md)*

Reads a file on the local disk and outputs the content as a string

Opposite of: [WriteToFile]

Useful combination: [PickStringPart]

Also see: [RequestUrl] which adds online capabilities and [GetAttributeFromJsonString]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **FilePath** (String) | Allows the selection of the file to be read |
| **TriggerUpdate** (Boolean) | Triggers a new scan of the selected file |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.String |

