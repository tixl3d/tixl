# WriteToFile

*in [Lib.io.file](README.md)*

Writes the incoming string into a predefined file on the local disk

Info: This operator is unable to create files. The file has to exist in order to write something into it.
Depending on the user and operating system, Tooll might need admin privileges.



Also see: [ReadFile] [RequestUrl] [FilesInFolder]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Content** (String) | Input / definition of the string that is being written into the file saved at the filepath |
| **Filepath** (String) | Path to the file that should be written into<br/><br/>Example: <br/><br/>Resources\user\YourUserName\FileName.txt<br/><br/>will open the FileName.txt and write the incoming string into the file |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.String |
| **OutFilepath** | System.String |

