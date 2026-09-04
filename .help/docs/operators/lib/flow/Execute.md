# Execute

*in [Lib.flow](README.md)*

Performs a sequence of drawing operations and then restores the graphics context. You can use it for grouping or activating and deactivating parts of your graphs.

Since [Execute] itself does nothing, you can also use it to name things by inserting it into the graph and setting the instance name by pressing Enter.

An alternative to [Execute] is [Group], which can also have a translation and multiple input connections.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command Required) | — |
| **IsEnabled** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

