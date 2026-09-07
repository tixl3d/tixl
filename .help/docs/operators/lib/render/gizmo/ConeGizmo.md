# ConeGizmo

*in [Lib.render.gizmo](README.md)*

Generates points for drawing a wireframe audio cone visualization matching BASS/ManagedBass 3D audio cone behavior. BASS cones are simple conical shapes defined by full angles (0-360 degrees). The cone extends from the origin in the -Z direction (forward). Generates line segments for: base circle and radial lines from apex to base. No rounded cap is used as BASS implements a hard cone boundary. Output points can be fed into DrawLines for rendering.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Angle** (Single) | The full cone angle in degrees (0-360). This is the total spread from edge to edge, not half-angle. |
| **Length** (Single) | The length of the cone from apex to base. |
| **Segments** (Int32) | Number of segments for the base circle. |
| **RayCount** (Int32) | Number of radial ray lines from apex to base (set to 0 for circle only). |

## Outputs
| Name | Type |
|---|---|
| **Points** | T3.Core.DataTypes.StructuredList |

