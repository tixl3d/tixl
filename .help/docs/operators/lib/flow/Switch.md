# Switch

*in [Lib.flow](README.md)*

Switches between connected graphs. Can be used to "cut" between scenes.
The index starts with 0 for the first input and will wrap on values exceeding the count of connected inputs.

Tip: 
- use index -1 to activate none (i.e. disable all)
- use index -2 to activate all

The naming of this operator diverges from the Pick... convention like [PickColor], [PickImage] etc because it also allows executing "nothing" or all connected inputs. 
This is a consequence of command being the only "type" that doesn't return data but only executes connected commands.

For a more visual approach see [TimeClip].


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Commands** (Command Required) | Scene Input |
| **Index** (Int32) | Selects which connection is active starting from 0.<br/>-1 deactivates all incoming scenes.<br/>-2 activates all incoming scenes.<br/><br/>Note that the index is wrapped: If you have two inputs connected, an index of 2 will route to the first. |
| **OptimizeInvalidation** (Boolean) | If enabled, Tooll will only invalidate the active connection. This can significantly improve performance for very complex scenes with thousands of Ops.<br/>This feature is experimental and might have unexpected side effects. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
| **Count** | System.Int32 |

