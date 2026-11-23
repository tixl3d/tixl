# Plan: Realtime marker tracking on video

Status: **Design only, nothing implemented.** Written 2026-11-23, revised after the first design review.

## Goal

Track markers in video footage on the GPU, in realtime and frame-exact during export. The first target is
**hand-placed natural features** (knots, nail holes, board corners on a fence), not prepared tracking dots.
One tracking result feeds three uses:

1. **Track buffer.** A GPU point buffer with a fixed frame count × marker count, filled while the video plays.
   Play it back like any point animation.
2. **Track file.** A human-readable file that can be saved, loaded and played back without the video or the
   tracker.
3. **Stabilization.** Derive a 2D transform per frame and use it to stabilize or match-move an image layer.

## Decisions

| Question | Decision |
| --- | --- |
| Target | Natural features, placed by hand. Template tracking (Lucas-Kanade) first; blob mode later. |
| Position space | Video space: centered, y up, y −1…1 spans the image height, x ±aspect. No knowledge of how the video is rendered. |
| Lost markers | Try to re-acquire automatically; needs testing. Separator output while lost. |
| File format | Human-readable: JSON Lines, one line per frame, compact number arrays. |

## What exists

| Piece | Where | Use |
| --- | --- | --- |
| New-frame signal | `PlayVideo.UpdateCount`, `VideoFrameResult.Produced` | Already there |
| Decoded frame index | `VideoPlaybackController._lastUploadedTarget` (frame-grid-snapped PTS), `FrameIndexForPts` | Private, needs exposing |
| Point layout | `Core/DataTypes/Point.cs`, `shared/point.hlsl` (64 bytes) | Track buffer element |
| Lost/separator convention | `Scale.X = NaN` (`Point.IsSeparator`) | Lost markers. Position stays finite. |
| Ring-buffer trails | `PointTrail` (compound, `points/sim/PointTrail-*.hlsl`), `PointTrailFast` | Reference only. Marker-major ring, not seekable by frame. |
| GPU readback | `StructuredBufferReadAccess` (async), `ReadPointsFromGpu` | File export, CPU transform |
| Dense optical flow | `OpticalFlow` (pixel-shader LK) | Math reference only; not a sparse tracker |

`DetectMotion` already has a `VideoFrameIndex` input, so the name `FrameIndex` fits.

## Step 1: Expose the frame index

Add `long FrameIndex` to `VideoFrameResult`. The controller sets it from `_lastUploadedTarget` via the same
round-to-grid mapping as `FrameIndexForPts`, on both the software and zero-copy paths. It is -1 before the first
frame. `PlayVideo` gets an `int FrameIndex` output, animated dirty flag. `VideoClip` gets the same later.

This index is the decoded frame's identity, not a count of updates. Scrubbing, looping and dropped frames all stay
correct. The tracker keys its state on it and does not use `UpdateCount`.

## Step 2: Video space

Tracks use one fixed space for every video, the same one a centered FitHeight quad has:

- Origin at the image center, **y up**.
- y spans −1 (bottom) to 1 (top) over the image height. x spans −aspect to aspect.
- From image UV (y down): `x = (2u − 1) · aspect`, `y = 1 − 2v`.

The tracker knows nothing about how the video is rendered. Layer2d is only one way to show it, and played-back
video gets no special treatment. To overlay markers, draw the points with the same transform as the image. That
is the user's graph, not the tracker's.

## Step 3: `[TrackMarkers]` compute op

### Inputs

- `Image` (video texture), `FrameIndex` (int)
- `SeedPoints` (buffer): marker positions in video space on `SeedFrame`. Marker identity = index in this buffer.
- `SeedFrame` (int): the frame the seeds were placed on
- `PatchSize` (px, default ~21), `SearchRadius` (px), `MinConfidence`, `ReacquireConfidence`
- `FrameCount`, `FirstFrame`
- `OrientationFrom`: `None`, `Movement`, `GlobalRotation`
- `Reset` (trigger), `IsEnabled`

### Outputs

- `TrackBuffer`: `FrameCount × MarkerCount` points, **frame-major** (`index = frame * markerCount + marker`).
  Frame-major lets a frame be read as one contiguous run, and new frames simply follow the last.
- `Points`: the current frame's `MarkerCount` points, ready to draw
- `SeedQuality`: one point per marker with the corner score in `F1` (see "Seed quality")
- `MarkerCount`, `TrackedFrameCount` (filled frames)

### Point encoding

| Field | Meaning |
| --- | --- |
| `Position.xy` | Video-space position (step 2) |
| `Position.z` | 0 |
| `F1` | Confidence 0–1 (zero-mean NCC against the key template) |
| `F2` | Marker id. Survives sorting and filtering. |
| `Orientation` | Z rotation: none, movement direction, or the global rotation solved from all markers |
| `Scale.xy` | 1 for now. Affine LK can fill it later. `Scale.X = NaN`: not tracked / lost |
| `Color` | Color sampled at the marker |

### Frame state machine (CPU, per update)

`f` is the incoming frame index and `last` is the last tracked frame.

- `f == last`: nothing to do.
- Slot `f` already filled: copy it to `Points`. Scrubbing back costs nothing, and the buffer works as a cache.
- `f == last + 1`, or slot `f - 1` filled: track forward from `f - 1`.
- Slot `f + 1` filled: track backward from `f + 1`. Scrubbing backward fills gaps.
- Otherwise: track from the nearest filled frame with a larger search radius. If confidence stays below
  `MinConfidence`, write separators.
- `f` outside `[FirstFrame, FirstFrame + FrameCount)`: output separators and leave the buffer unchanged.

During export, every frame arrives exactly once and in order, so the track is deterministic.

### Shader passes

All passes are dispatched only when the frame changed. The tracker is idle while paused.

1. **Luma pyramid.** Write luma to an R16F texture with mips (3–4 levels).
2. **Track.** One thread group per marker.
   - Prediction: the previous position plus velocity. When at least two other markers are valid, use the
     global similarity transform solved from them instead. On rigid backgrounds like the fence this predicts much
     better than per-marker velocity.
   - Pyramidal Lucas-Kanade, translation only, 2–3 iterations per level, frame `f-1` → `f`.
   - Drift check: zero-mean NCC of the result against the **key template** (the patch from the seed frame,
     refreshed only when confidence is high). Zero-mean NCC ignores brightness changes, so it copes with shadows
     passing over the fence.
3. **Write** into slot `f` and to `Points`.

Cost: tens of markers × 21 px patches × 4 levels is far below a millisecond. Hundreds of markers in realtime should
be feasible.

### Seed quality

Hand-placed seeds fail in two predictable ways, both visible in the first test footage:

- **Edges (aperture problem).** A patch on a straight board edge can only be followed across the edge, not
  along it. Compute the smaller eigenvalue of the structure tensor (Shi-Tomasi) over the patch and output it as
  `SeedQuality`. Draw weak seeds in a warning color. An optional `SnapSeeds` moves each seed to the best corner
  within a few pixels.
- **Image border.** A seed near the video's edge (the top marker against the letterbox black) has a patch that
  partly covers the static border, which pins the track. Mark seeds whose patch leaves the image as invalid.

### Re-acquiring lost markers

When a marker's confidence drops below `MinConfidence` (for example the cat crosses it), it becomes lost:

- Its position keeps being predicted from the global transform of the other markers.
- Each frame, search for the key template around the prediction with a wider radius (full NCC search on a
  coarse mip, then LK to refine).
- It becomes valid again only above `ReacquireConfidence`, a higher threshold than `MinConfidence`. The gap
  between the two stops flicker.
- While lost it outputs a separator. An option `KeepPredicted` writes the predicted position with confidence 0
  instead, for match-moves that must not jump.

Needs testing on real footage; it is a separate phase.

## Step 4: Playback and conversion ops

- `[SampleTrack]`: track buffer + `MarkerCount` + frame (float, or time + fps) → current points. Interpolates
  linearly between frames and treats a separator on either side as lost. This is the "play the markers back" op.
- `[TrackToTrails]`: transposes to marker-major with a separator after each marker, like `PointTrail`'s sorted
  output, so markers can be drawn as motion paths with `DrawLines`.

## Step 5: Track file (human-readable)

JSON Lines (`.track.jsonl`). The first line is the header; every further line is one frame. Each frame is a single
line, so recording appends lines, a cut-off file still loads, and diffs stay readable.

```
{"Version":1,"Fps":25,"FirstFrame":0,"SourceSize":[1080,1920],"Source":"footage/cat.mp4",
 "Markers":[{"Id":0,"Name":"knot-left"},{"Id":1,"Name":"board-top"}],
 "Fields":["x","y","confidence","angle"]}
{"f":0,"m":[[-0.2131,0.0312,1.0,0.0],[0.0642,0.4201,0.97,0.0]]}
{"f":1,"m":[[-0.2125,0.0330,0.99,0.0],null]}
```

(The header is shown wrapped; in the file it is one line.)

- `null` = lost. This becomes a separator on load.
- `x, y` are in video space (step 2). With `SourceSize` in the header any tool can convert back to pixels.
- 4 decimals keep sub-pixel precision at 4K. Size is about 30 bytes per marker per frame: 10 markers × 3,000
  frames ≈ 1 MB.
- Full `Point` structs stay GPU-side; the loader rebuilds them (id → `F2`, confidence → `F1`, angle → `Orientation`).
- `[SavePointTrack]`: async readback through `StructuredBufferReadAccess`, write on a user trigger or keep
  appending while recording. Blocking readback only during export.
- `[LoadPointTrack]`: outputs `TrackBuffer`, `MarkerCount`, `FrameCount`, `Fps`, the same contract as
  `[TrackMarkers]`. `[SampleTrack]` and `[TrackToTrails]` work on either.

No new slot type for now. A `PointTrack` type should come out of the structured-data work (`StructuredList` leaf
with metadata).

## Step 6: Stabilization and layer alignment

### First use case

The `_ResearchCatAnimation` project shows the need. Five `[Point]` ops are combined into a buffer and drawn as
fixed crosshairs (targets). The video's Layer2d has hand-keyed Position, plus single Rotate and Scale keys, so
that fence features stay on the crosshairs. The keys drift between keyframes. Tracking should replace this hand
keying.

With ScaleMode FitHeight and Scale 1, video space (step 2) is exactly Layer2d's local space. A 2D similarity
transform therefore maps one-to-one onto Layer2d's `Position`, `Rotate` and `Scale` inputs, with no scale-mode
math.

### `[SolveLayerTransform]`

Inputs: track buffer + `MarkerCount` + `FrameIndex`, and `TargetPoints` (world space, one per marker; the
crosshairs). Outputs: `Position` (Vector2), `Rotate` (degrees), `Scale` (float), ready to connect to Layer2d.

For panning footage, the fixed targets generalize to a world anchor per marker; see
[Anchors per marker](#anchors-per-marker). The crosshairs become the anchors of the hand-placed seeds.

- Solve per frame: the similarity that moves the tracked features (video space) onto the targets, weighted by
  confidence, skipping lost markers. 1 marker: translation only. 2 or more: least squares (Umeyama 2D).
  Toggles turn off rotation or scale.
- **Seeds from targets.** On `SeedFrame` the user aligns the layer once by hand. Seeds are then the targets mapped
  back through that one transform. The user places five points once, not twice.
- **No readback lag on replay.** The first live pass reads solved transforms back asynchronously (1–2 frames
  late). Each solve is cached per frame index on the CPU, so later playback, scrubbing and export read the exact
  transform for the frame with no lag. Export blocks on readback for frames that aren't cached yet.
- Live connection only. No keyframe generation in the tracker; if keys are needed later, the existing
  `[SetKeyframes]` op can record the solved outputs.

### `[StabilizeImage]`

`[StabilizeImage]` image op: image + track buffer + `MarkerCount` + `FrameIndex` + `ReferenceFrame`.

1. **Solve** in a one-group compute pass. This is the same similarity solve as above, with the reference frame's
   tracked positions as targets. 4 markers (later): homography / corner pin, for screen replacement.
2. **Smooth** (optional). The buffer holds the whole track, so smoothing can average the transform over a centered
   window of ±k frames. That stabilizes only the jitter and keeps the intended camera move. Realtime tracking only
   knows the frames up to now; this works fully on a pre-tracked or loaded track.
3. **Resample** the image with the inverse transform in a pixel shader. `Strength` (0–1) blends between
   stabilized and original.

The same solve is the global-motion prediction used in step 3, so it is written once as a shared shader include.


## Panning footage and automatic markers

Decided 2026-11-23: the primary goal is **stabilization**; tracking specific features comes later. Automatic
marker placement is a **mode of `[TrackMarkers]`**, not a separate op.

### Backward tracking

Already works in the tracker: an untracked frame next to a tracked one is tracked from it in either direction,
and the velocity prediction follows the direction. Reverse playback itself is slow because each step back
re-decodes from the previous keyframe (reverse read-behind is open in `Plan_VideoFollowups.md`). Until that
lands, track from the `.proxy.mov` proxy, which seeks cheaply.

### Forward-backward check

After tracking `f → f+1`, track the result back to `f`. If it misses the start position by more than about a
pixel, the marker is lost. This is the standard way to catch occlusion and slipping in Lucas-Kanade trackers. It
costs one more Lucas-Kanade pass per marker.

### `SeedMode`: Manual / Automatic

- **Manual** (current): fixed markers from `SeedPoints` on `SeedFrame`.
- **Automatic**: a pool of `MarkerCount` slots. Each frame, slots whose marker is lost or left the image are
  refilled with new corners:
  - Shi-Tomasi score (the same as `SeedQuality`), one best candidate per grid cell.
  - Cells holding a live marker are skipped, so markers stay spread across the image.
  - Refilling happens on the GPU, with no readback.
- A refilled slot starts a new marker: the trail gets a separator, and F2 becomes a unique marker id (slot plus a
  generation counter) instead of the slot index.
- Later, both modes can share one buffer: hand-placed markers in the first slots, the automatic pool after them.

### Anchors per marker

With panning, fixed targets leave the view, so the solve fits each marker to its own **world anchor**, set when
the marker starts:

- Automatic mode: the anchor is the marker's position on its first frame, mapped through the transform already
  solved for that frame. The first frame uses identity. No manual alignment is needed.
- Manual mode: anchors come from the one manual alignment on `SeedFrame`, as before.

The solved transform is then the accumulated camera motion over arbitrarily long pans. It drifts slowly with each
handoff, which is normal for this method.

### Stabilization means smoothing the path

Pinning panning footage to its anchors would cancel the pan and push the picture out of frame. Stabilization
applies `smoothed(path) ∘ inverse(path)`: it keeps the intended camera move and removes the jitter. Smoothing over
a centered window needs frames ahead, which the track buffer has once a range has been tracked. In a live first
pass, use a trailing window, which lags a little. Smoothing therefore belongs in the same phase as the solve.

### Leaving the view vs. occlusion

Both are handled by re-acquisition (predict from the global transform, then search). Leaving the view only adds a
rule: don't search while the predicted position is outside the image. In automatic mode, a marker that left is
usually replaced instead.

## Phases

1. ~~`FrameIndex` on `PlayVideo`~~ (done, `d6bce9b43`).
2. ~~`[TrackMarkers]` + `[SampleTrack]`~~ (done 2026-11-23 in `Lib/Symbols/point/track/`, shaders in
   `shaders/points/track/`; works on the cat footage). Differences from the design above:
   - No `OrientationFrom` yet; orientation is identity.
   - Gaps up to 8 frames are bridged with one extra pyramid level. Larger jumps aren't tracked, so a failed
     long-range track never gets cached; step or play from a tracked frame instead.
   - Showing an already tracked frame refreshes the recent template, so tracking can continue from any
     cached frame.
   - The key template is never refreshed yet, so confidence drops on strong zoom or rotation.
   - Moving a seed re-seeds only that marker on the next visit to `SeedFrame`. Only `Reset`, a different marker
     count, or a change to the range or patch size clears the whole track. Every frame shown is advanced per marker:
     a marker the frame already has is kept, a missing one is tracked from the neighboring frame. This fills in
     re-seeded markers without touching the others.
   - Lost points keep their last searched position and their confidence in F1 for diagnosis (Scale.X is still NaN).
3. Forward-backward check in `[TrackMarkers]`.
4. `[SolveLayerTransform]` with a world anchor per marker, a cache per frame, and path smoothing. Test on static
   footage (the fence) first.
5. `SeedMode` Automatic: corner detection on a grid, slot refill, unique marker ids. Test on panning footage.
6. Re-acquiring lost markers (shared similarity solve; skip predictions outside the image).
7. `[StabilizeImage]` (GPU resample) if driving Layer2d isn't enough.
8. `.track.jsonl` save and load, `[TrackToTrails]`.
9. Seed editing in the output window, built on the geometry ops' point editing.
10. Later: per-marker seed frames for hand-picked features, blob mode for dots, affine Lucas-Kanade
   (per-marker rotation and scale), homography / corner pin, reverse read-behind in the video decoder.

## Resolved

- **Seeds:** derived from the crosshair targets and one manual layer alignment on `SeedFrame`. A dedicated
  point editor comes later and overlaps with geometry point editing.
- **Live vs. bake:** live connection to Layer2d only. No keyframe generation; `[SetKeyframes]` covers that as a
  workaround.
- **Primary goal:** stabilization first; tracking specific features later.
- **Automatic markers:** a `SeedMode` of `[TrackMarkers]`, not a separate op.
