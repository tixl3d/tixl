# BuildRandomString

*in [Lib.string.random](README.md)*

Produces a wide selection of text writer effects that 
can be linked to AudioReactions. Interesting setups are

[MockStrings]->[GetStringPart]->[ScrambleString]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **WriteMode** (Int32) | — |
| **Clear** (Boolean) | — |
| **Insert** (Boolean) | — |
| **JumpToRandomPos** (Boolean) | — |
| **InsertString** (String) | — |
| **Separator** (String) | — |
| **OverwriteOffset** (Int32) | — |
| **MaxLength** (Int32) | — |
| **WrapLines** (Int32) | — |
| **WrapLineColumn** (Int32) | — |
| **ScrambleRatio** (Single) | — |
| **Scramble** (Boolean) | — |
| **ScrambleSeed** (Int32) | — |
| **OverrideBuilder** (StringBuilder) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.String |
| **Builder** | System.Text.StringBuilder |

