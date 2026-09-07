# GpuMeasure

*in [Lib.render.analyze](README.md)*

Measures the time in milliseconds that the GPU (graphics card) needs to render the current image.

Similar to the Performance display top left next to the menu in TiXL's UI.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command) | Scene input to be measured |
| **Enabled** (Boolean) | Activates the measuring |
| **LogToConsole** (Boolean) | If activated, all measurement results are displayed in the console. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

