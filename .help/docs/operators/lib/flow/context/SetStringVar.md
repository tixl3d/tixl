# SetStringVar

*in [Lib.flow.context](README.md)*

Writes a string value to the context's float variable dictionary.
Use [GetStringVar] to read this value further down the graph.

Example: [SetContextVariableExample]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **StringValue** (String) | String value of this variable |
| **VariableName** (String) | The name to set for the variable. So it can be referenced to later in the graph. |
| **SubGraph** (Command) | Optional sub-graph so this can be used between different command operators (Execute, etc.). |
| **ClearAfterExecution** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

