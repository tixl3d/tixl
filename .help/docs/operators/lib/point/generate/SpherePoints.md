# SpherePoints

*in [Lib.point.generate](README.md)*

Generates a sphere with evenly distributed points on its surface.

Needs a [DrawPoints], [DrawLines], or [DrawMeshAtPoints] or similar in order to become visible.

Similar: [GridPoints], [RadialPoints], [PointsOnMesh]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Count** (Int32) | Controls the amount of points distributed on the sphere |
| **Radius** (Single) | Controls the radius of the sphere |
| **Center** (Vector3) | Moves the center of the sphere |
| **StartAngle** (Single) | Rotates the sphere |
| **Scatter** (Single) | Shifts the points on the sphere with varying values<br/><br/>ProTip: If the points should leave the surface of the sphere, [RandomizePoints] can be of help |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

