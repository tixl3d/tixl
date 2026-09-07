# BoundingBoxPoints

*in [Lib.point.generate](README.md)*

Generates the bounding box containing the points used as input / Indices : Center - 0, Min - 1, Max - 8.

In order to access its points' position, use [PointsToCPU] with [GetPointDataFromList].

It iterates through the source points, excluding those with invalid W values (-nan(ind)), and calculates the minimum and maximum bounds in each dimension (x, y, z). These bounds define the eight points of the bounding box: the minimum and maximum corners of the box. Additionally, it computes the middle point of the bounding box by averaging the minimum and maximum bounds

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

