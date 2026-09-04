# HexGridPoints

*in [Lib.point.generate](README.md)*

Creates a buffer of GPU points distributed on a hexagonal grid.
Same as [GridPoints] with TilingMode set to 'HoneyCombs' (but with fewer options).

Tips:
- Set any of the counts to 1 to create a plane of points.

Needs a [DrawPoints], [DrawLines] or [DrawMeshAtPoints] or similar in order to become visible.

Similar: [RadialPoints], [SpherePoints], [PointsOnMesh]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **CountX** (Int32) | — |
| **CountY** (Int32) | — |
| **CountZ** (Int32) | — |
| **Size** (Vector3) | — |
| **Scale** (Single) | — |
| **Center** (Vector3) | — |
| **W** (Single) | — |
| **OrientationAxis** (Vector3) | — |
| **OrientationAngle** (Single) | — |
| **Pivot** (Vector3) | — |
| **SizeMode** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

