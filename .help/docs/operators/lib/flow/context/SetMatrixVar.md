# SetMatrixVar

*in [Lib.flow.context](README.md)*

Stores a matrix as an array of vector4 on the evaluation context.
This is used internally by passing transform matrices between render passes for things like rendering shadow maps.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SubGraph** (Command Required) | — |
| **VariableName** (String) | — |
| **Value** (Vector4[] Required) | — |
| **ClearAfterExecution** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

