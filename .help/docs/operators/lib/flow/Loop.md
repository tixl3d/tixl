# Loop

*in [Lib.flow](README.md)*

Executes the sub graph multiple times. This can be very useful for iterating a drawing function.

You can use [GetFloatVar] and [GetIntVar] to access the iterator variables. 

Please note that this operation creates draw calls for each iteration and thus is not meant for loop counts exceeding 1000 or more.

Please check the example.




## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command Required) | — |
| **Count** (Int32) | — |
| **IndexVariable** (String) | — |
| **ProgressVariable** (String) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

