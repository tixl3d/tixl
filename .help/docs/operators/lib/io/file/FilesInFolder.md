# FilesInFolder

*in [Lib.io.file](README.md)*

Scans a folder for all existing files and creates a list that can be used with [PickFromStringList]
It also counts files in a folder and outputs the amount as an integer.

See the example [FilesInFolderExample] and [FadingSlideShow] for what might be a useful combination

Other interesting related ops: [ReadFile] [RequestUrl] [PickStringPart] [WriteToFile] [GetAttributeFromJsonString]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Folder** (String) | Defines the selected folder / path |
| **Filter** (String) | All file names in the specified folder are searched for the string specified here. They are only selected if they match. Examples: .jpg .obj .png .mp4 .txt images- slideshow_ animated_cat-  and so on |
| **TriggerUpdate** (Boolean) | Triggers a scan of the selected folder and filter / filetype |

## Outputs
| Name | Type |
|---|---|
| **Files** | System.Collections.Generic.List`1[System.String] |
| **NumberOfFiles** | System.Int32 |

