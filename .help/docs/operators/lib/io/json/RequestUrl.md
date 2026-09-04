# RequestUrl

*in [Lib.io.json](README.md)*

Loads a URL and outputs the source text as a string.

Hint: The string contains what you see when you open the URL with a browser and select 'View Page Source'

Useful combinations: [PickStringPart]

Also see [LoadImageFromUrl] [GetAttributeFromJsonString]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Url** (String) | Defines the URL which should be contacted<br/>e.g. https://cataas.com/cat |
| **TriggerRequest** (Boolean) | Triggers a refresh of the defined URL to update the result |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.String |

