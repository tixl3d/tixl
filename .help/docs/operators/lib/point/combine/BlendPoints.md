# BlendPoints

*in [Lib.point.combine](README.md)*

Creates different transitions between two different point setups

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **PointsA_** (BufferWithViews Required) | Input for Point Setup A |
| **PointsB_** (BufferWithViews Required) | Input for Point Setup B |
| **BlendFactor** (Single) | Defines the transition between the two point setups<br/>0 = PointsA is displayed without changes<br/>1 = PointsB is displayed without changes<br/>If the 'BlendMode' is set to 'Blend', the points overshoot at values lower than 0 and higher than 1.<br/>If the 'BlendMode' is set to 'RangeBlendSmooth', the transition loops. |
| **Scatter** (Single) | Adds random noise to the points' position during the transition |
| **BlendMode** (Int32) | Defines how to blend between the point setups |
| **RangeWidth** (Single) | Defines the range width when the 'BlendMode' is set to 'RangeBlend' or 'RangeBlendSmooth' |
| **Pairing** (Int32) | Selects the mode with which points are paired for blending |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

