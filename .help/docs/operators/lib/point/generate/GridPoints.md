# GridPoints

*in [Lib.point.generate](README.md)*

Creates a buffer of GPU points distributed on a rectangular or hexagonal grid.

Tips:
- Set any of the counts to 1 to create a plane of points.
- Switch the SizeMode to set if the scaling refers to the padding between the points or the size of the volume.

Needs a [DrawPoints], [DrawLines] or [DrawMeshAtPoints] or similar in order to become visible.

Similar: [RadialPoints], [SpherePoints], [PointsOnMesh]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **CountX** (Int32) | — |
| **CountY** (Int32) | — |
| **CountZ** (Int32) | — |
| **SizeMode** (Int32) | — |
| **Size** (Vector3) | — |
| **Scale** (Single) | — |
| **Center** (Vector3) | — |
| **Pivot** (Vector3) | — |
| **Tiling** (Int32) | — |
| **PointScale** (Single) | — |
| **F1** (Single) | — |
| **F2** (Single) | — |
| **Color** (Vector4) | — |
| **OrientationAxis** (Vector3) | — |
| **OrientationAngle** (Single) | — |
| **W** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

