# SampleCurve

*in [Lib.numbers.curve](README.md)*

Creates a window in the 'graph' in which a curve editor with a single curve is created. 

This curve can be edited (in the 'Parameters' view) in the same way as the animation curves in the standard timeline.

Functions:

- Left mouse click: Select points
- Left-click and drag: Move points and their Bezier points
- Left-click + alt: Create new point on the curve
- Right-click: Options (extra and interpolation etc.)
- Right-click dragging: Panning the view inside the curve editor
- Clicking the Icon in the upper left corner opens the curve in a bigger window

ProTip: If the 'result' from an [AnimValue] is connected to the U-Input, you get an easily editable automatically animated curve with many options

Info: Selecting and moving this operator in the graph can sometimes be difficult. To the left of the CurveEditor there is a surface (with anti-slip coating ;) ) that is best suited for selecting and moving the operator.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Curve** (Curve) | Curve Input / Editor |
| **U** (Single Relevant) | Input for controlling the horizontal axis |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |
| **CurveOutput** | T3.Core.DataTypes.Curve |

