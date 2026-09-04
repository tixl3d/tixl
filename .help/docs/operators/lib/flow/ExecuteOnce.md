# ExecuteOnce

*in [Lib.flow](README.md)*

Executes the subgraph only once. This can be useful for initializing buffers or resetting states.

This is obsolete. Please consider using [Execute] or [RenderTarget] with [Once] connected to IsEnabled.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command) | — |
| **Trigger** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
| **OutputTrigger** | System.Boolean |

