# Lib.point.draw

## Operators

- [**DrawBillboards**](DrawBillboards.md) — Draws points and billboards or quads. This operator is very flexible and allows for a wide spectrum of effects.
- [**DrawConnectionLines**](DrawConnectionLines.md)
- [**DrawLines**](DrawLines.md) — Draws a point buffer as lines. The lines will be aligned to the camera, but their width will shrink with distance to the camera. You can override this with the ScaleWithDistance parameter. We use the point’s W attribute as a scale factor for the line width. If the W attribute of a point is NaN (Not a Number), that point is not being drawn and acts as a separator between the adjacent line segments. This allows a point buffer to contain multiple disconnected line segments.
- [**DrawLinesAlt**](DrawLinesAlt.md) — Alternative version of [DrawLines] that allow to draw closed shapes by connecting the first and the last points.
- [**DrawLinesBuildup**](DrawLinesBuildup.md) — Renders incoming points as growing strokes. The points' W attribute encodes the U progress of the extension of the strokes.
- [**DrawLinesShaded**](DrawLinesShaded.md)
- [**DrawMeshAtPoints2**](DrawMeshAtPoints2.md) — Similar to [DrawBillboards], this operator draws meshes instead of images.
- [**DrawMovingPoints**](DrawMovingPoints.md)
- [**DrawPoints**](DrawPoints.md) — Draws a point buffer with the set camera, transform, and fog. The points are drawn as camera-facing billboards, ignoring the point orientation. The W attribute of the points is used for scaling. This can be controlled with the UseWForSize parameter.
- [**DrawPoints2**](DrawPoints2.md) — A new version of [DrawPoints] that uses a Radius parameter instead of Size.
- [**DrawPointsDOF**](DrawPointsDOF.md)
- [**DrawPointsShaded**](DrawPointsShaded.md) — Draws a point buffer as PBR-shaded spheres using the attributes defined by [SetMaterial].
- [**DrawRayLines**](DrawRayLines.md) — A special line renderer that draws camera-facing 3D geometry lines without corner metering that can intersect the near plane.
- [**DrawRibbons**](DrawRibbons.md) — Draws a point buffer as ribbons. The lines will distance to the camera. You can override this with the ScaleWithDistance parameter.
- [**DrawTubes**](DrawTubes.md) — Draws a shaded 3D mesh for connected lines points.
- [**VisualizePoints**](VisualizePoints.md) — This helper operator visualizes points and their orientation. It is permanently visible, but you can toggle the default settings for these gizmos using the "Gizmo" toggle in the output window.

---

*Auto-generated from the operator library.*
