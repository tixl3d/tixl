# LaserCamera

*in [Lib.laser.camera](README.md)*

**Universal geometry-to-laser converter and a standard TiXL camera in one.**

Like the common [Camera] op it has its own lens (selectable in the output window camera dropdown,
mouse-drivable), and every point, line and mesh drawn inside the **Scene** connected to it is captured
and encoded into a laser frame - dots, line segments and mesh wireframes merged, with the `Group` and
transform scopes active at draw time applied.

Point-cloud content becomes isolated dwell dots, line strips become blank-separated segments, and
meshes become wireframes of their unique edges. The projection is driven by the op's own aim
(Position/Target or Use Rotation angles, FieldOfView) - or, with a camera wired into
**CameraReference**, by that camera's view and lens, whose aim inputs then take over. **OutputRef**
only routes the frame to a laser output of the output setup, whose driver streams it to the bound
device.

Like [SendToOutput] it is a supplier of the output setup: it registers itself, lists as CONTENT
there, and can be frozen with **Update**. The **Output** command renders nothing - laser data
leaves only through the **LaserPoints** chain, and the command path carries the gizmo. The scene is
captured once per laser pump, not per rendered frame.

See the [LaserCamera guide](../../../../using/laser/LaserCamera.md) for the capture and encoding
rules (dots, line segments, mesh wireframes, frame budget).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Scene** (Command) | Subtree to capture. Everything drawn inside it merges into the laser frame. |
| **CameraReference** (Object) | Reference output of another camera. Connected, that camera's view and lens drive the projection and this op's own aim inputs are ignored. |
| **OutputRef** (Guid) | The laser output of the active setup this camera is routed to; empty means unrouted. |
| **Update** (Boolean) | Off holds the last frame and stops evaluating the scene. |
| **Position** (Vector3) | Camera position. Ignored while a CameraReference is connected. |
| **Target** (Vector3) | Camera look-at target. Ignored while a CameraReference is connected. |
| **Up** (Vector3) | Camera up vector. Ignored while a CameraReference is connected. |
| **UseRotation** (Boolean) | Aim by Rotation angles around Position instead of Target. Ignored while a CameraReference is connected. |
| **Rotation** (Vector3) | Pitch/Yaw/Roll in degrees for UseRotation. Ignored while a CameraReference is connected. |
| **FieldOfView** (Single) | Vertical field of view in degrees. Ignored while a CameraReference is connected. |
| **Roll** (Single) | Camera roll in degrees. Ignored while a CameraReference is connected. |
| **AspectRatio** (Single) | -1 uses the square scanner field; an explicit value enables anamorphic framing. Ignored while a CameraReference is connected (the scanner field stays square). |
| **ClipPlanes** (Vector2) | Near and far planes. Geometry outside them is dropped. Ignored while a CameraReference is connected. |
| **DotDwellPoints** (Int32) | Lit repeats per isolated dot (1..64). Brighter/stabler dots cost frame budget. |
| **MaxEdgeStep** (Single) | Sample spacing for lines and wireframe edges in 16-bit laser units. |
| **PrintToLog** (Boolean) | Log per-source counts and projected bounds for diagnostics. |

## Outputs
| Name | Type |
|---|---|
| **LaserPoints** | T3.Core.DataTypes.StructuredList |
| **Output** | T3.Core.DataTypes.Command |
| **Reference** | System.Object |

**See also:** [MeshToLaserPoints](../convert/MeshToLaserPoints.md), [PONKInput](../input/PONKInput.md) and the laser outputs in the [Output Setup](../../../../using/OutputSetup.md)

---
*Generated from operator definition in Lib.laser.camera*
