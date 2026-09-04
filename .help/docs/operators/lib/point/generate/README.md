# Lib.point.generate

## Operators

- [**BoundingBoxPoints**](BoundingBoxPoints.md) — Generates the bounding box containing the points used as input / Indices : Center - 0, Min - 1, Max - 8.
- [**CommonPointSets**](CommonPointSets.md) — Provides a set of useful point lists that can be used to draw shapes, lines and other geometric forms.
- [**DoyleSpiralPoints2**](DoyleSpiralPoints2.md) — Generate a set of points with decreasing sizes that can be used to draw a Doyle spiral:
- [**GridPoints**](GridPoints.md) — Creates a buffer of GPU points distributed on a rectangular or hexagonal grid.
- [**HexGridPoints**](HexGridPoints.md) — Creates a buffer of GPU points distributed on a hexagonal grid.
- [**LinePoints**](LinePoints.md) — Define points from a source position to a direction.
- [**MeshVerticesToPoints**](MeshVerticesToPoints.md) — Creates a point at each vertex of the connected mesh.
- [**PointInfoLines**](PointInfoLines.md) — Generates a line point buffer that can visualize numerical point attribute data.
- [**PointTrail**](PointTrail.md) — Same as [PointTrail] with added internal features for [DrawBillboards]
- [**PointTrailFast**](PointTrailFast.md) — Keeps previous copies of points in a cycling buffer that can be used to draw trails and other effects.
- [**PointsOnImage**](PointsOnImage.md) — Uses the image brightness to emit points.
- [**PointsOnMesh**](PointsOnMesh.md) — Get evenly distributed points on a mesh. Note that the initial evaluation of the mesh is extremely slow and should not be done on every frame.
- [**RadialPoints**](RadialPoints.md) — A versatile generator of circular point sets that can create a variety of circles, spirals, helixes, etc.
- [**RepeatAtPoints**](RepeatAtPoints.md) — Repeats a list of GPU points at positions provided by another list of points. The orientation of the target points can be applied, so this operator can be used to create point instantiation.
- [**RepetitionPoints**](RepetitionPoints.md) — Generate a list of points by repeating a transform operation.
- [**SpherePoints**](SpherePoints.md) — Generates a sphere with evenly distributed points on its surface.
- [**SubdivideLinePoints**](SubdivideLinePoints.md) — Inserts additional points between line points.

---

*Auto-generated from the operator library.*
