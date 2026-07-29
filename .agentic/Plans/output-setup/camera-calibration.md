# Output Setup — Camera-Assisted Calibration (plan, 2026-07-29)

Camera-in-the-loop calibration for projection setups: a calibrated webcam observes the venue and
automates what is hand-clicked today — projector solves, corner pins, alignment to physical features,
drift detection, and latency measurement.

**Status: long-term.** Sequenced after the [`ui-restructuring-plan.md`](ui-restructuring-plan.md)
phases. One cheap enabler (point annotations, §4.1) is worth landing early. Related:
[`multi-machine.md`](multi-machine.md) (remote outputs during calibration).

---

## 1. Capabilities (three distinct features, one camera)

### 1.1 Camera pose from reference features
Match the image patch around each **point annotation** between the stored reference photo and the live
camera frame → 2D↔3D correspondences (annotation positions are known in meters via the surface /
reference binding) → PnP solve for the camera's pose in venue space.

- Right tool for scenes with real texture (facades, structured stages).
- Wrong tool for featureless white surfaces — that's what structured light is for (1.2).
- Reuses the annotation + `ReferenceBinding` model; only the *point* annotation kind is new (§4.1).

### 1.2 Projector calibration via structured light (the workhorse)
Project Gray-code / binary patterns per output, capture → dense projector↔camera pixel
correspondence. Needs **no scene texture** (the pattern brings its own features). From it:

- **Planar surfaces:** per-surface homographies → auto corner-pin (`OutputMapping.Quad`).
- **Full solve:** with camera pose known, correspondences feed the existing
  `ProjectorCamera.CalibrationPoints` + `ProjectorSolver` path — auto-generated instead of
  hand-clicked, same storage, same residual reporting.
- **Dense correction:** camera-derived warp lattices and blend masks land in the *planned*
  `OutputMapping.Warp` / `Mask` fields (data-model §2.3). Constraint on that design: the lattice
  format must accept solver-written points (the resample-on-resolution-change behavior already
  planned is compatible).

Field precedent: MadMapper spatial scanner, Lightform scan, TouchDesigner CamSchnappr.

### 1.3 Timing / latency measurement
Project a temporal marker (flash or binary frame counter), detect it in the capture → end-to-end
latency of render → present → projector chain.

- Accuracy honestly bounded by the camera: ~half a camera frame per trial (rolling shutter, 30/60 fps);
  averaging repeated trials gets to a few ms. Good for "2 frames or 5 frames behind", not sub-frame
  genlock (photodiode/hardware territory — out of scope).
- The measured constant is what makes scheduled presentation meaningful
  (composition-swapchain present-at-time, see `multi-machine.md` §5).

### 1.4 Drift detection (re-calibration light)
With a posed camera and stored reference patches: recapture, verify the projection still lands on its
features; on mismatch warn and offer the rotate/pan quick-fix (the "someone bumped the tripod" wish in
`long-term-features.md`) or a re-solve. Likely the highest show-night value in the whole feature —
calibrate once, drift detection saves the gig.

---

## 2. Model mapping (all additive)

| Concept | Home | Notes |
|---|---|---|
| Camera | `Setup.Cameras[]` (new) | Venue entity, mirror of an output: `Id, Name, Intrinsics{focal, principal, distortion[]}, Pose?` — intrinsics from the wizard (§3.1), pose solved (1.1) or manual. |
| Physical device | `MachineConfig` (new list next to `DeviceBinding`) | Which webcam on which machine — same venue/machine split as outputs. |
| Capture | per-camera captures (new) | `{ CameraId, Pose, frames }` — a camera can shoot from several positions; never assume one fixed viewpoint. Frames are transient (not serialized); poses persist. |
| Point annotation | `ReferenceBinding.Annotations` | New string-discriminated annotation kind next to lines (§4.1). |
| Solve results | existing fields | `ProjectorCamera.CalibrationPoints/ResidualPx`, `OutputMapping.Quad`, later `Warp`/`Mask`. No new storage for results — solvers write what manual editing writes, so undo (`SetupSnapshotCommand`) and the UI work unchanged. |

**Package isolation rule:** the capture/CV subsystem goes in its own operator package or editor-side
service — never coupled into Lib's hot-reload path (FFmpeg precedent). Build on the capture layer from
`Plan_VideoDeviceInput`.

---

## 3. Pipeline phases (each independently shippable)

1. **Intrinsics wizard** — checkerboard/ChArUco capture flow, writes `Camera.Intrinsics`. Guided UI
   (live detection feedback, coverage hints). Also establishes the camera-control requirements (§5).
2. **Structured-light projector solve** — pattern sequencing on an output + synchronized capture +
   Gray-code decode → correspondences → planar corner-pin first (biggest wow per effort), full
   `ProjectorSolver` path second. Requires: display patterns on the target output (remote outputs need
   the multi-machine state channel — cross-dependency noted in `multi-machine.md` §3).
3. **Camera pose from reference patches** (1.1) — patch matching around point annotations + PnP.
   Unlocks solves in venue coordinates and 4.
4. **Drift check** (1.4) — recapture, compare, warn/quick-fix/re-solve.
5. **Timing measurement** (1.3) — nearly free once capture + pattern display exist.

Dense warp/mask solving is *not* a phase here — it rides on the §2.3 mapping-stack work whenever that
lands, consuming phase-2 correspondences.

---

## 4. Enablers (cheap now, decoupled from the rest)

### 4.1 Point annotation kind — **can land on the current branch**
`ReferenceBinding.Annotations` holds lines today. Add a point kind (position + label), editable on the
reference/straighten canvas like line endpoints (same `CanvasPointHandle` skeleton, same undo command
family). Independently useful for manual work (named survey points, measurement anchors) before any
CV exists.

**Superseding shape (2026-07-29):** [`Plan_StageExtension.md`](../Plan_StageExtension.md) §5 promotes these to a
setup-level `Setup.ReferencePoints[]` entity with an *observation list* (stage-model position,
per-image positions, camera detections) — one point observed in many spaces is the correspondence hub
every solve consumes. Build the setup-level entity from the start; only *line* annotations stay
binding-local (they serve rectification, not correspondence).

### 4.2 Reserved shape for `Setup.Cameras`
Nothing to build — just don't collide: keep "Cameras" free as a top-level setup key, and keep the
venue/machine split intact for any new device-ish concept.

---

## 5. Risks & constraints

- **Camera discipline:** auto-exposure / auto-white-balance / auto-focus must be lockable or decoding
  and matching get flaky. Adopt a "supported camera" posture (UVC devices with manual controls);
  surface locked/unlocked state in the UI.
- **Ambient light & dynamic range:** structured-light capture wants dim ambient; detect poor decode
  confidence and say so rather than producing a bad solve silently.
- **Texture dependence** of patch matching (1.1) — always offer the structured-light path as fallback.
- **Coverage:** one FOV rarely sees a whole venue → multi-capture model from day one (§2).
- **Scope gravity:** this is a CV subsystem (decode, PnP, bundle-ish solve, matching). Dependency
  decision (OpenCV-class library vs. extending the in-repo math stack — `Homography`/`ProjectorSolver`
  already cover more than expected) is the first technical spike of phase 1. Keep it in its own
  package either way.

## 6. Open questions

1. Library: extend in-repo numerics vs. take an OpenCV dependency (spike in phase 1; distortion
   models and ChArUco detection are the hard-to-hand-roll parts).
2. Where calibration UI lives: wizard dialogs vs. a canvas mode (leaning: Calibrate mode grows a
   camera sub-mode; wizards only for intrinsics).
3. Multi-projector overlap solving (shared correspondences → blend masks) — phase 2 extension or part
   of the §2.3 mapping-stack work.
4. Whether drift check can run continuously (camera permanently mounted) vs. on-demand only.
