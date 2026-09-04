# Group

*in [Lib.render.transform](README.md)*

Groups a sequence of incoming draw commands.

Although similar to [Execute], it also allows to [Transform] and override the color multiply for all operators further down (i.e., left) in the graph.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Commands** (Command Required) | — |
| **Translation** (Vector3) | Moves the incoming subgraph<br/><br/>X (-left / +right), <br/>Y (-down / +up), <br/>Z (-forward / +backwards)<br/> |
| **Rotation** (Vector3) | Rotates the incoming subgraph around the following axes:<br/><br/>X: Horizontal axis<br/>Y: Vertical axis<br/>Z: Forward axis<br/> |
| **Scale** (Vector3) | Scales the incoming subgraph in the following directions:<br/><br/>X: Width<br/>Y: Height<br/>Z: Depth<br/> |
| **UniformScale** (Single) | Uniformly scales the incoming subgraph<br/> |
| **IsEnabled** (Boolean) | Enables / Disables the group |
| **Color** (Vector4) | Everything in this group will be rendered with this color multiplied. |
| **ForceColorUpdate** (Boolean) | Forces constant updating if the colors animation is ignored. |
| **EnableProfiling** (Boolean) | Enabling this option will measure the time it took to update the content of this group.<br/>This can be very useful for profiling and optimizing your designs.<br/><br/>You can see the measurements in the "IO Window".  |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

