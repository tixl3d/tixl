# SampleGradient

*in [Lib.numbers.color](README.md)*

Defines a gradient that can then be sampled into a color or used further by [GradientsToTexture] or other operators.
Left-clicking the Gradient in the graph- or in the parameters window adds a color sample.
Dragging a color sample to the lower border removes it.

The first output returns the sampled color.
The second output the gradient structure.

Right-click in the parameter window gradient to adjust interpolation mode or save presets.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SamplePos** (Single Relevant) | Defines which color is given out |
| **Gradient** (Gradient) | Defines the color gradient<br/><br/>Left-click adds a new color sample<br/>Dragging the color sample down deletes it |
| **OverrideInterpolation** (Boolean) | — |
| **Interpolation** (Int32) | Defines how the color is interpolated between the different samples on the gradient |

## Outputs
| Name | Type |
|---|---|
| **Color** | System.Numerics.Vector4 |
| **OutGradient** | T3.Core.DataTypes.Gradient |

