# DoyleSpiralPoints2

*in [Lib.point.generate](README.md)*

Generate a set of points with decreasing sizes that can be used to draw a Doyle spiral:

In the mathematics of circle packing, a Doyle spiral is a pattern of non-crossing circles in the plane in which each circle is surrounded by a ring of six tangent circles. 
These patterns contain spiral arms formed by circles linked through opposite points of tangency, with their centers on logarithmic spirals of three different shapes. 

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Steps** (Int32) | Amount of points along the spiral |
| **Offset** (Single) | Offsets the position of the drawn points on the spiral |
| **PointsPerStep** (Int32) | Defines how many points one step consists of |
| **SpiralSteepness** (Int32) | Defines how intense the size of the points increases with each row |
| **Scale** (Single) | Uniformly scales the size |
| **ScaleBias** (Single) | Non-uniformly scales the position of the points on the spiral<br/>The points in the center get compressed while the outer points get pushed outwards |
| **CenterPositionScale** (Single) | Scales the center of the spiral cutting off all affected points |
| **W** (Single) | Scales the W-Value of the points (default = size of the points) |
| **WBias** (Single) | Defines how intense the outer points grow in size |
| **CenterSizeScale** (Single) | Scales the inner points of the spiral cutting off points by reducing their size |
| **Center** (Vector3) | Transforms the center / pivot of the spiral |
| **OrientationAxis** (Vector3) | — |
| **OrientationAngle** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

