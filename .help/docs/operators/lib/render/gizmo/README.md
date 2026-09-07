# Lib.render.gizmo

## Operators

- [**ConeGizmo**](ConeGizmo.md) — Generates points for drawing a wireframe audio cone visualization matching BASS/ManagedBass 3D audio cone behavior. BASS cones are simple conical shapes defined by full angles (0-360 degrees). The cone extends from the origin in the -Z direction (forward). Generates line segments for: base circle and radial lines from apex to base. No rounded cap is used as BASS implements a hard cone boundary. Output points can be fed into DrawLines for rendering.
- [**DrawBoxGizmo**](DrawBoxGizmo.md) — Creates a 3D wireframe cube that can be used as a bounding box
- [**DrawCamGizmos**](DrawCamGizmos.md) — An attempt to draw camera gizmos.
- [**DrawLineGrid**](DrawLineGrid.md) — Renders a grid of lines in the given scaling.
- [**DrawSpatialAudioGizmos**](DrawSpatialAudioGizmos.md) — Draws gizmos for all spatial audio players in the current composition. Visualizes source and listener positions, attenuation ranges, and directional cones.
- [**DrawSphereGizmo**](DrawSphereGizmo.md) — Creates a 3D wireframe globe that can be used as a bounding box
- [**GridPlane**](GridPlane.md) — A helper gizmo that can use a fragment shader to draw smooth grid lines.
- [**Locator**](Locator.md) — Can be used to add a visible transform gizmo and its position in a scene (only visible if 'Toggle Gizmos and Floor Grid' is enabled in the Output window).
- [**PlotValueCurve**](PlotValueCurve.md) — Plots a history curve of a value.
- [**VisibleGizmos**](VisibleGizmos.md) — Only executes the subgraph if the set visibility matches the visibility of the evaluation context.

---

*Auto-generated from the operator library.*
