# GetFloatVar

*in [Lib.flow.context](README.md)*

Fetches a float variable from the context.

Use [SetFloatVar] to set the variable further up the graph. Also [GetIntVar]

Important:
If you're using this together with [Loop], don't forget to use 'f' as the normalized progress variable.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **VariableName** (String) | Note: When used with [Loop] this is frequently "f". |
| **FallbackDefault** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |

