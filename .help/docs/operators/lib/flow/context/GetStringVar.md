# GetStringVar

*in [Lib.flow.context](README.md)*

Fetches a float variable from the context.

Use [SetStringVar] to set the variable further up the graph.

Important:
If you're using this together with [Loop], don't forget to use 's' as the normalized progress variable.

Example: [SetContextVariableExample]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **VariableName** (String) | The name of the variable we want to get the string value from. This is set using [SetString] |
| **FallbackDefault** (String) | The String value that will used if there is no input |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.String |

