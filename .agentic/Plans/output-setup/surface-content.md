# Output Setup — Content Designed on a Surface (plan, 2026-11-07)

Make the straightened reference photo of a surface the design guide for its realtime content: a printed
label on the wall becomes the place a [Text] op is put, and that text lands on the label when projected.
Four use cases of rising ambition, one graph shape, built in four shippable phases.

**Status (2026-11-07): phases 1 and 3 (route 1) shipped and used on a real projector.** Phase 3 grew a projector
section on outputs: Is Projector, a Model dropdown with user presets and 26 researched built-in ones
(`EditorResources/projector-presets.json`, each with source and note), lens shift, a zoom range with a Ratio | Zoom switch
and a zoom slider, and a side view whose distance, wide/tele picture heights and lower-edge gap can be measured with a
tape. Align Points gained gamepad confirm/cancel (A/B), release (X), find-point flash (Y, also on switching points) and
a trigger-held inverted photo. Open: phase 2 ([SurfaceSpace]), phase 4 ([AlignmentPoints]). Phase 3 as built:
`ProjectorSolver.TrySolveFromPlane` (Core) with a fallback zoom for squarely seen surfaces; `ProjectorCamera.LensAxis`,
`SolvedFromSurfaceId`, `IsZoomEntered`; the output card's Projector Camera section (`ProjectorCalibration`); the
surface card's Facing preset. Field note: on the 38 × 58 cm SpeakerBoxes pin the solved distance ranged 1.0–2.8 m
over the full range of lens shift, so a small, nearly frontal surface needs the lens shift right; a large angled one
doesn't. The editor shows the stored `LensAxis` as lens shift in % of the picture size, the way spec sheets give it. Deviation: [SurfaceReferenceImage]
has no `Color` input — the [Layer2d] that draws it already tints and fades.
 Prompted by a projection-mapping demo (2026-11-06) where steps 2–5 below
had to be done by hand and step 6 was not possible at all. Related: [`camera-calibration.md`](camera-calibration.md)
(projector pose), [`../Plan_StageExtension.md`](../Plan_StageExtension.md) (3D stage), [`use-case-flows.md`](use-case-flows.md)
(Flows 3 and 7 touch the same ground).

---

## 1. The flow today

1. Trace a straight region on a reference photo (works).
2. Create a [SendToOutput] with the surface's aspect ratio (by hand: compute width × height).
3. Connect it to the region (outliner drag).
4. Connect the region, or its parent, to the output (outliner drag; the region/parent distinction is a known
   trap, see §9).
5. Add reference points and align them on the wall (Align Points, works).
6. **Design the content against the photo** — missing. The photo lives in the editor's Board; the graph has no
   access to it, so content is placed by trial and error on the wall.

## 2. Principles

- **Content is designed in surface metres.** A surface already has a size in metres and an anchor; content
  placed in that frame lands at its physical place on the wall, at its physical size.
- **One content group, several consumers.** The same [Group] of user content feeds the previz (with the
  reference photo) and the output branch (without it). The photo of the wall is never projected onto the
  wall, and that follows from the graph's shape rather than from an "editor only" flag.
- **The surface is referenced by Guid** (an op input like [StageGeometry]'s `Canvas`), never inferred from
  what the send happens to be connected to.
- **Cameras stay hidden where they can.** Orthographic cameras are awkward to work with directly; where a
  camera is needed it is part of an op with surface-level parameters.

## 3. Use case A — the photo as a 2D design guide

Physical size doesn't matter; the photo is a backdrop to place content by eye.

```
[SurfaceReferenceImage(Surface)] → [Layer2d] ─┐
[Text] → [Layer2d] ───────────────────────────┴→ [Group] → [RenderTarget] → [SendToOutput(From Surface)] → Surface
```

- [Layer2d]'s vertical fit already maps the image to the frame, so no camera is needed.
- **New: [SurfaceReferenceImage]** outputs the straightened photo fragment of a surface or region as a
  texture, cropped exactly to it, with a `Color` input to fade or tint it.
- **New: [SendToOutput] resolution mode** `Fixed | From Output | From Surface` (see §7.1), so the render target
  gets the surface's aspect without arithmetic.
- The reference layer is in the output branch here; the user mutes it (or sets its opacity to 0) before the
  show. Acceptable for A; B and C solve it structurally.

## 4. Use case B — physical size on one surface

A [Text] 12 cm tall over a 12 cm label.

```
[Text], [LoadObj], ... → [SurfaceSpace(Surface, Viewer)] → [RenderTarget] → [SendToOutput(From Surface)] → Surface
```

**New: [SurfaceSpace]** — a multi-input Command op (it replaces the [Group], so the user still has a scene to
add to) that:

- sets a **window camera**: an off-axis frustum whose image plane is exactly the surface's rectangle, in metres
  from its anchor, Y up;
- draws the straightened photo fragment as a quad on that plane, depending on `ShowReference` (§10);
- takes `Viewer`: **Straight On** (the eye infinitely far away — an orthographic frustum) or **From Position**
  (a viewer point in front of the surface — an off-axis perspective frustum).

For content in the surface plane both viewers give the same image; they differ only for things standing off
the wall. So the orthographic camera is the far-away special case of one camera, not a mode the user has to
understand.

## 5. Use case C — the reference wall in a 3D scene

See the wall, with its photo, in 3D, with props such as a [LoadObj] of a person pointing at it, and render the
same scene to the projector.

```
                     ┌─ [Group: SurfaceGeometry(photo) + Content] → editor viewer (previz, any camera)
[Content] ── Group ──┼─ [SurfaceSpace(Surface, From Position)] → [RenderTarget] → [SendToOutput] → Surface
                     └─ [UseProjectorCam(Projector)] → [RenderTarget] → [SendToOutput] → Output
```

The join between scene and send is a camera, and which one depends on **whose eye** the scene is rendered for:

- **The viewer's eye** (per surface): [SurfaceSpace] with `From Position`. A prop in front of the wall reads as
  standing there for an audience at that position: the anamorphic illusion. [ProjectOntoStage] already does
  this for an environment image; this is the live-scene version.
- **The projector's eye** (per projector): [UseProjectorCam] exists and renders the whole projector frame from
  the projector's pose. No corner pins and no per-surface sends; content on any wall in stage metres lands
  correctly. **Blocked on a solved projector pose:** it uses the hand-entered `OutputDefinition.Camera`, and
  `ProjectorSolver` (normalized DLT, needs points on more than one plane) exists but nothing calls it yet.

`SurfaceGeometry` here is [StageGeometry] with its `ReferenceImage` texture coordinates, or a single-surface
variant of it. The surface needs a stage placement for C; A and B work in the surface's own frame.

## 6. Use case D — projecting onto an object on the surface

A 3D-printed face mask is fixed at a known place on the aligned wall (or floor). Its OBJ is loaded, and a particle
simulation runs tears down the face. Off the plane, a corner pin is wrong (a point 8 cm in front of the wall,
lit 25° off-axis, misses by about 3.7 cm), so this needs the projector as a real camera: pose and intrinsics.

**The object stays in the graph.** Loading, placing and processing geometry is operator land; the setup does not
store meshes, files or object poses.

```
[LoadObj(mask)] → [Transform(surface metres)] ─┬─ [Tear simulation] ─┐
                                               └─ [Draw black (occluder)] ┴→ [Group] → [UseProjectorCam(Projector)]
                                                                               → [RenderTarget] → [SendToOutput] → Output
```

Rendering from the projector's own eye makes occlusion correct for free: what the projector can't see, it can't
light, so tears behind the nose stay off the cheek.

**What the setup adds is small:**
- **Alignment preset** on a surface — `Wall | Floor | Ceiling`, an editing rig over the existing
  `Surface.Placement` pose. For a single-surface setup the aligned surface *is* the world: origin at its anchor,
  its plane as declared. No floor plan or stage origin needed.
- **A solved projector camera** in the existing `OutputDefinition.ProjectorCamera` (`Pose`, `Lens` with field of
  view and `LensShift`, `CalibrationPoints`, `ResidualPx`).

**Two routes to the projector's intrinsics:**
1. **One plane plus the lens shift.** With known intrinsics, one aligned plane gives the pose exactly. A plane
   yields two constraints on the intrinsics: enough for the focal length (zoom), not for the lens shift too. So
   the user enters the lens shift (spec sheet, or the common "0" / "100% up"), and TiXL solves zoom and pose
   from the points already aimed. The throw ratio from the spec sheet, if known, is a cross-check. Placement of
   the mask is then whatever the physical mount matches the [Transform].
2. **Points on the object itself.** A small op publishes 3D points from the graph as reference points (§7.8):
   a few vertices of the mask (nose tip, chin, eye corners). Aimed on the wall like the surface's own points,
   they make the point set non-planar, and `ProjectorSolver` solves pose and intrinsics without a spec sheet.
   The solve absorbs small errors in where the mask was placed, into the projector camera; if the mask then
   moves relative to the wall, wall content and mask content disagree slightly. That argues for a fixed mount,
   not for moving the mask into the setup.

**Consequence:** once a projector has a solved camera, each surface's corner pin can be derived from it (the
pin is where the surface's corners project). Flat content through sends and 3D content through
[UseProjectorCam] then line up on the same projector by construction, with one calibration per projector.

## 7. Building blocks

### 7.1 [SendToOutput] resolution follows the surface (phase 1)
- As built: a `SizeFrom` input (`Resolution | Surface`) on [SendToOutput]; `Resolution` keeps today's behaviour
  (the Resolution input, or the output's size at 0×0), so existing projects are unchanged.
- Mode `From Surface`: resolution = the surface's size in metres × the output pixels per metre where it is
  projected densest (longest projected edge per direction, the larger density of the two), so the render keeps
  the surface's aspect and lands at least 1:1. Decided 2026-11-07 after the first run: `PixelsPerMeter` is a
  default (400), not a measurement, and gave a 38 cm box 153 × 230 px while it covered ~770 × 1150 projector
  pixels. `PixelsPerMeter` remains the fallback for a surface on no output yet. A `ResolutionFactor` input
  (default 1, 0.25–4) scales any derived size, surface or output, so the automatic size can be overruled
  without giving up following the surface; a typed-in Resolution stays exact.
- Second surface mode, added the same day: `SurfaceAspect` keeps the height the send would render at anyway
  (Resolution, else the output's) and takes the width from the surface's metres. The projected mode changes
  size with every alignment move, which resets feedback buffers while points are dragged; the aspect mode
  doesn't, and is what Connect... sets. Enum: `Resolution | ProjectedSurface | SurfaceAspect` (stored 0 | 1 | 2).
- The compositor already requests a patch's own size (`OutputContentResolver.TryGetFittedRequest`); the same
  path gives a surface's size to a send whose mode asks for it.
- A send shown on several surfaces of different aspects warns, as the send already does for several outputs.

### 7.2 The straightened photo in Core (phase 1)
`TryRenderStraightened` lives in `Editor/Gui/Windows/OutputSetup/SetupOutputView.Photo.cs`, so no op can use it.
Move the straighten render (trace quad → surface rectangle, with an optional surround) into
`Core/Output/Rendering`, next to `OutputCompositor.RenderWarpedTexture`, and have the Board, the calibration
discs and [SurfaceReferenceImage] share it. Region cropping reuses `NarrowPhotoUvToPinned`'s maths, which
also moves. This is a Core addition, justified because operators need it.

### 7.3 [SurfaceReferenceImage] (phase 1)
Inputs: `Surface` (Guid), `Surround` (0 = cropped to the surface), `MaxSize`. Output: Texture2D. Status warning
when the surface has no trace. As built: `Core/Output/Rendering/StraightenedPhoto.cs` holds the straighten,
`Operators/Lib/Symbols/render/output/SurfaceReferenceImage.cs` the op.

### 7.4 [SurfaceSpace] (phase 2)
Inputs: `Command` (multi), `Surface` (Guid), `Viewer` (Straight On / From Position), `ViewerPosition`
(metres; in the surface frame without a stage placement, in stage space with one), `ShowReference`,
`ReferenceColor`. Draws the photo quad from the shared straighten render in 6.2.

### 7.5 Projector pose from aligned points (phase 3)
Correspondences = aimed reference points: each point's 3D position (annotation position on its surface through
the surface's stage placement) ↔ its aim on the projector canvas. Feed them to `ProjectorSolver` and store the
result as the output's camera; [UseProjectorCam] then works without hand-entered numbers.
- Needs points on at least two planes. With a single wall, the pose needs known lens intrinsics (or a
  zoom-free assumption); decide in phase 3.
- Once a projector has a pose, every surface's corner pin could be derived from it. That is the durable form of
  "the alignment belongs to the projector, not the connection" (today's per-connection pins are parked on
  disconnect, which fixes the data loss but not the duplication).

### 7.6 Guidance: "Connect..." on an unconnected surface (phase 1)
When a surface or region without content is selected, the output view's toolbar shows **Connect...** with:
- **New Content for This Surface** — adds the use-case A chain ([SurfaceReferenceImage] → [Layer2d] → [Group]
  → [RenderTarget] → [SendToOutput(From Surface)]), a slice, the surface link and, when the surface's parent
  is already mapped or there is exactly one projector, the output mapping. Ends with Align Points open and the
  new [Group] selected in the graph. From phase 2 on, it offers the [SurfaceSpace] chain instead (B).
- **Use Existing Content...** — the sources, as a list.
- **Slices from Regions** — the existing action, for a surface with regions.
- One undo step, as `RegionSlices.Create` does (MacroCommand of graph edits + setup snapshot).

The manual steps of §1 become: select region → Connect... → New Content → align points → design.

### 7.7 Alignment preset (phase 3)
A `Wall | Floor | Ceiling` dropdown in the surface's parameters, writing `Surface.Placement`. A surface without
a placement is treated as the world origin for its own projector solve. Applies to use cases C and D.

### 7.8 [AlignmentPoints] (phase 4)
Takes a point buffer (for example selected vertices of a loaded mesh) and registers each point as a 3D
reference point for Align Points, the way [SendToOutput] registers content. Align Points lists them next to the
surface's points; aiming works the same. The setup stores only the aims, keyed by the op's Guid plus the point
index — a stored link, not inferred — and feeds them to the projector solve as `CalibrationPoints`. The setup
never stores the mesh, its file or its pose.

## 8. Phases

| Phase | Delivers | Use case |
|---|---|---|
| 1 | §7.1 send resolution mode, §7.2 straighten in Core, §7.3 [SurfaceReferenceImage], §7.6 Connect... | A |
| 2 | §7.4 [SurfaceSpace] with the window camera; Connect... switches to it | B, C per surface |
| 3 | §7.5 projector pose from aligned points, with the entered lens shift (route 1); §7.7 Alignment preset | C per projector, D route 1 |
| 4 | §7.8 [AlignmentPoints]: object points in the projector solve (route 2) | D route 2 |

Each phase ships on its own: help page, manual test set, release-notes entry.

## 9. Adjacent work from the same demo

Not part of this plan, but the Connect... flow leans on them:
- ~~**Projection rows in the outliner**~~ — dropped 2026-11-07: the outliner's connection line already shows a
  surface's mapping onto an output, and parked mappings keep its alignment through reconnecting.
- **Region dropped on an output or patch** — pin the parent when the parent isn't on that output yet, and let
  the region ride it (today the region gets a full-patch pin of its own and its slice stretches over the
  parent's area).
- Shipped 2026-11-07: parked mappings, the steadier point solver, input slots and add/replace drop zones.

## 10. Interface audit (ops land in user projects)

Plausible next uses, and whether the shape above absorbs them:
- Several regions of one wall in one [SurfaceSpace] — `Surface` would become a list; or one [SurfaceSpace] per
  region feeding one send. **Decide before phase 2 ships.**
- A reference matte (content masked to the label's area) — an extra output on [SurfaceReferenceImage]; additive.
- A curved or non-planar surface — needs the warp of `data-model.md`; the window camera does not apply. Out of scope.
- Live camera feed instead of the photo — another texture source behind the same op; additive.
- An object that moves (a mask held by a performer) — needs tracking feeding the object's [Transform]; the
  projector camera from §7.5 stays valid. Additive, graph-side.
- Several projectors on one object — one solved camera per projector, each through its own [UseProjectorCam];
  blending the overlap is the open masks/warp work. Additive.
- [AlignmentPoints] fed by something other than a mesh (a laser-measured list, a tracked marker) — any point
  buffer works; additive.
- Hiding the reference on real outputs by default — needs a context flag the compositor sets when pulling for a
  presented output. Make `ShowReference` an enum (`Always | Editor Only | Never`) from the start rather than a
  bool, so `Editor Only` can be added without a migration.

## 11. Open questions

1. Names: [SurfaceSpace] vs [SurfaceScene] vs [OnSurface]; [SurfaceReferenceImage] vs [SurfacePhoto].
2. Can the compositor tell a presented output from an editor preview cheaply (for `Editor Only`)?
3. Window camera without a stage placement: viewer position in surface metres along the surface normal — is
   that understandable, or should `From Position` require a placed surface?
4. Phase 3 with a single plane: assume fixed intrinsics, or ask for a second plane (floor)?
5. Route 1's lens shift: entered as a percentage of the image height (spec-sheet style) or picked from presets?
6. How does a user select mesh vertices for [AlignmentPoints] — an existing point-selection op, or picking in a
   3D view? Picking is the main UI cost of route 2.
7. ~~Projected pixel size or `PixelsPerMeter`?~~ Projected, see §7.1. Open: the render size now changes while
   points are aligned, which re-allocates the render target during a drag.
