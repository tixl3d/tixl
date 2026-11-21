# LaserCamera

`Operators/Lib/Symbols/laser/camera/LaserCamera.cs`

Geometry-to-laser converter **and** a standard TiXL camera in one: it aims with its own lens (Position/Target or Use Rotation angles, FieldOfView - selectable in the output window camera dropdown, mouse-drivable, with a gizmo) or follows the camera wired into **CameraReference**, while every point/line/mesh drawn inside the **Scene** connected to it is captured and encoded into a laser frame - dots, line segments and mesh wireframes merged, with the `Group`/transform scopes active at draw time applied.

Like [SendToOutput](../OutputSetup.md) it is a supplier of the Output Setup: it registers itself, lists as **CONTENT** there, and is routed to a laser output. The frame is streamed by the setup's laser driver, not by a graph operator.

## What it does

1. Builds its camera matrices from its own inputs - or, with a camera wired into **CameraReference**, borrows that camera's view and projection matrices and ignores its own aim inputs. The laser frame **is** the scanner field, so the default aspect (-1) is square; an explicit aspect is honored for anamorphic framing.
2. Evaluates the **Scene** subtree with a `GeometryCapture` collector on the `EvaluationContext`. Draw ops register their geometry instead of rasterizing it (`CaptureOnlyDraws`), so the visible viewport is never painted with this camera's matrices.
3. Reads the registered buffers back to the CPU once per scene and frame - always one frame behind, never stalling on the GPU - and shares that capture with every other laser camera wired to the same scene.
4. Projects every position through `ObjectToWorld * WorldToCamera * CameraToClipSpace`, dividing by the clip-space W like any lens. Points outside the near/far planes or the frame are dropped, segments are **clipped** at the frame instead of pinned to its border, and NaN or infinite positions are never drawn.
5. Encodes each source as its primitive (see below) up to the point budget of the routed output (a CITP feed carries at most 12,000 points per datagram), and publishes the frame as `StructuredList<LaserPoint>` on **LaserPoints**.

The **Command** output renders nothing: re-running the scene once per rendered frame per camera would put its GPU readbacks outside the pump's budget. It only carries the gizmo.

## Output Setup

* **Routing.** A new camera attaches itself to a free laser output. Drop it on another laser output in the flow outliner (or set **OutputRef**) to route it elsewhere; each camera keeps its own viewpoint and an output is never shared by two cameras.
* **Update.** Off freezes the camera on its last frame and stops evaluating its scene, like the **Update** toggle of a SendToOutput. The content card in the Output Setup edits the same input, undoably; the outliner marks a frozen camera.
* **Status.** The op reports a missing **Scene**, an unrouted camera and a failed update on its node.
* Optimizer, safety and calibration are properties of the laser **output**, not of the camera.

## Following another camera

Wire the **Reference** output of a camera (the common [Camera], an [OrbitCamera], another LaserCamera) into **CameraReference** to let that camera drive the laser view:

* The projection uses the referenced camera's view and lens (position, target, up, roll, field of view, lens shift), and this op's own **Position**, **Target**, **Up**, **UseRotation** / **Rotation**, **FieldOfView**, **Roll**, **AspectRatio** and **ClipPlanes** are ignored.
* The reference is re-evaluated on every pump, so an animated orbit or a mouse-driven camera moves the laser live.
* The op's gizmo follows the referenced camera, but its position/target handles disappear - the aim belongs to the referenced camera.
* The scanner field stays **square**: the referenced camera's aspect is not taken over, so a 16:9 render camera shows a square crop of its view instead of a horizontally squeezed picture (circles stay circles on the scanner).

## Capture rules

* Capture registration lives in the low-level **`Draw`/`DrawInstancedIndirect` ops** (and `_DispatchSceneDraws`), because those are command slots that re-run **every frame** - the data ops that bind buffers (`GetBufferComponents`, ...) are frozen by TiXL's dirty-flag caching in static subtrees and would never re-run.
* Point vs. line content is told apart **after readback**: line strips written by `PairPointsForLines` carry NaN-scale separator points, point clouds don't. Meshes are recognized by their `PbrVertex` stride plus paired index buffer.
* The same buffer drawn repeatedly under the same transform is captured once; under a different transform it is captured again. Max 256 entries per frame (truncated beyond that).
* Only buffer-based geometry is captured - content rasterized purely into images is invisible to the laser.
* Laser-frame data drawn back into the scene (LaserPointsToBuffer previews) is excluded, so the camera never captures its own output.

## Dots

Every projected point becomes an **isolated dot**: a blanked jump to it, then `DotDwellPoints` lit repeats. Points with a larger `Scale` draw as a small circle instead, for brightness. Because the move happens with the beam off, scattered point clouds are never connected by lit lines (see [Points vs. Lines](Points%20vs.%20Lines.md)).

## Line segments

TiXL line buffers are strips: consecutive points form segments, and breaks are marked by separator points (`Scale.x = NaN/Inf`, as written by `PairPointsForLines`). A segment is drawn only if neither endpoint is a separator and it has real length, sampled every `MaxEdgeStep` units. A segment that starts where the previous one ended continues it, so a polyline costs a single blanked approach; any other segment gets its own, so separate lines are never connected by lit strokes. Sized segments draw as thin rectangles.

## Mesh wireframe

Each *unique* triangle edge (shared edges deduplicated, flat-surface diagonals dropped) becomes an isolated lit run: blanked approach point, then straight-line samples every `MaxEdgeStep` units (256 samples max per edge, 65536 edges max). Scene-captured meshes are transformed by their draw-time `ObjectToWorld`. Color comes from the edge's start vertex `ColorRgb`.

## Inputs

| Input | Default | Purpose |
|-------|---------|---------|
| `Scene` | - | Subtree to capture - everything drawn inside merges into the laser frame |
| `CameraReference` | - | Reference output of another camera ([Camera], [OrbitCamera], a rig's master [LaserCamera]); connected, that camera's view and lens drive the projection |
| `OutputRef` | - | The laser output of the active setup this camera is routed to |
| `Update` | on | Off holds the last frame and stops evaluating the scene |
| `Position` / `Target` / `Up` | (0,0,2.4141) / origin / +Y | Camera placement (ignored with a CameraReference) |
| `UseRotation` / `Rotation` | off / 0 | Aim by Pitch°/Yaw°/Roll° around `Position` instead of a target point (ignored with a CameraReference) |
| `FieldOfView` / `Roll` / `ClipPlanes` / `AspectRatio` | 45° / 0° / 0.01..1000 / -1 | Projection; aspect -1 = square scanner field (ignored with a CameraReference) |
| `DotDwellPoints` | 8 | Lit repeats per dot (1..64) - brighter/stabler dots vs frame budget |
| `MaxEdgeStep` | 4000 | Sample spacing of lines and wireframes in 16-bit units |
| `PrintToLog` | off | Log per-source counts, sample points and world/projected bounds per update |

## Outputs

| Output | Purpose |
|--------|---------|
| `LaserPoints` | The frame (`StructuredList<LaserPoint>`), including blanked approaches and dwell repeats |
| `Output` | Gizmo callback only - renders nothing |
| `Reference` | This instance as `ICamera` for camera-reference ops |

## Usage notes

* If nothing comes out, check that the camera actually looks at the content: points behind the camera or outside the frame are dropped, and `PrintToLog` reports world and projected bounds.
* Scene capture costs one CPU readback per unique buffer and frame - keep scenes lean.
* Instanced meshes (DrawMeshAtPoints) capture as one template mesh, not N instances.
* Color: laser colors ignore transparency/materials - the point/vertex color channel is used directly.
* The frame this operator outputs is what **all** transports ([EtherDream](EtherDream.md), [PONK](PONK.md), [CITP](CITP.md)) stream - see [Points vs. Lines](Points%20vs.%20Lines.md) for why the image matches across them.
