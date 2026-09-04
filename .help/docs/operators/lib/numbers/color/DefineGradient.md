# DefineGradient

*in [Lib.numbers.color](README.md)*

Defines a gradient from a set of colors and positions.
This can be useful for animating gradients.

Positions outside the range of 0 ... 1 will be ignored.

Consider using [BuildGradient] for a more flexible solution.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Color1** (Vector4) | — |
| **Color1Pos** (Single) | — |
| **Color2** (Vector4) | — |
| **Color2Pos** (Single) | — |
| **Color3** (Vector4) | — |
| **Color3Pos** (Single) | — |
| **Color4** (Vector4) | — |
| **Color4Pos** (Single) | — |
| **Interpolation** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **OutGradient** | T3.Core.DataTypes.Gradient |

