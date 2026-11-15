# Plan: Implicit Render Defaults (camera, depth, normal for post effects)

Status: **not started**, written 2026-11-13 on `main` (bd30fd200). Nothing built or run; findings come from reading.
Code checks and editor tests done 2026-11-14 (see "Research Findings").
**M0 implemented 2026-11-14, untested by a human** (test set `.tests-manual/moving-points-repeated-evaluation.md`):
`Operators/Lib/Utils/EvaluationHistory.cs` keys history by (context, `__MotionBlurPass`, call index within that pass);
`_TransformsCBuffer` and `KeepPreviousPointBuffer` use it. Both report no motion when the evaluation wasn't seen last
frame. With `Keep` off (paused, repeated motion blur pass) `KeepPreviousPointBuffer` shows the matching first-pass
evaluation, as before. Per-context keying replaced the planned `KeepsHistory` flag, so Core is unchanged.
**M1 implemented 2026-11-14, untested by a human** (test set `.tests-manual/velocity-buffer.md`), done before
Phases 1–3 with manual wiring:
- `[RenderTarget]`: `WithVelocityBuffer` / `VelocityBuffer` (RGBA16F, SV_Target2, cleared to 0). Normal and velocity
  targets share one nested `ColorAttachment` class. With velocity on, a normal target always holds slot 1: the Vulkan
  backend packs bound targets (`VulkanCommandList.BeginRendering` skips nulls), so a gap would move velocity into the
  normal slot.
- Layout in `Assets/shaders/shared/velocity-functions.hlsl`: rg = current uv − previous uv **without TAA jitter**
  (writers add back `JitterUv(current) − JitterUv(previous)`; TXAA applies its jitter correction only on the camera
  path; stored with jitter it flickered by up to a pixel per frame, reported by the user 2026-11-15),
  b = 1 − depth when written (occlusion check against the depth buffer), a = 1 when written, 0 = unknown.
  - Alpha is not data: `PickBlendMode`'s blend states don't enable independent blending, so every target blends
    like the color target. With a = 1, alpha blending overwrites; additive blending corrupts b and fails the check.
  - Depth is stored as 1 − depth because scene depth crowds near 1, where half floats step by ~0.0005.
  - No previous depth: for moving pixels TXAA's disocclusion test compares view depth instead of world positions.
    Surfaces moving in depth faster than `DepthRejection` per frame lose history (no AA, no ghosting).
  - Screen space rather than world space: it is what TAA, motion blur and upscalers consume, and it needs fewer
    channels.
  - Verified over the bridge 2026-11-14: buffers allocate per toggle (velocity keeps a normal target), 4× MSAA
    without errors, TXAA with an empty velocity map matches the unconnected one within frame-to-frame jitter noise.
- `[TXAA]`: `VelocityMap` input (t3). Where the velocity is valid it replaces the camera reprojection; elsewhere and
  when unconnected the shader takes the old path.
**M2 started 2026-11-15** for `[DrawMesh]`: `mesh-Draw.hlsl` binds `_TransformsCBuffer.PrevBuffer` as `b6`, passes
current and previous clip positions and writes `EncodeVelocity` to SV_Target2. `[DrawScene]` shares the shader but
binds only b0–b4, so b6 reads 0 and `EncodeVelocity` reports no motion known. Mesh deformation is not covered (rigid
transforms and camera only).
- Verified over the bridge 2026-11-15 (Vulkan with validation, debug returns in TXAA, reverted afterwards):
  - A rotating `[DrawMesh]` writes velocity, and TXAA decodes it on the mesh (detected area = area with a = 1, depth
    mismatch ~0).
  - Static cube, moving camera: object velocity matches TXAA's camera-only estimate within ~4·10⁻⁶ uv while the camera
    moves ~10⁻³ uv per frame, so sign, scale and jitter handling agree.
  - Not shown yet: the visible gain. On a flat-shaded cube TXAA's neighborhood clamp hides wrong history, and fast
    rotation exceeds `MotionThreshold`, so TXAA with and without velocity both stay close to the raw frame. Needs a
    textured or detailed mesh.
  - Seen once per new TXAA instance: a Vulkan validation error for TXAA's `History` texture (sampled while still in
    color-attachment layout). Not touched by M1/M2; to be checked separately.
    The same error appears for `[ScreenSpaceAO]`'s `Accumulated` / `AccumulatedFrameCount` on its first frame
    (2026-11-15), so it is the feedback-texture pattern in general (a target read on the frame it was created),
    not TXAA specifically.

## Goal

Post effects that need to know how their image was rendered (and, later, how it moved) — `[ScreenSpaceLighting]`, `[GodRays]`, `[TXAA]`,
`[MotionBlur]` — should find the camera, depth map and normal map by themselves. Manual connections stay possible
and always win.

Example: `mesh → [Camera] → [RenderTarget] → [ScreenSpaceLighting]` works without wiring `CameraReference`,
`DepthMap` and `NormalMap`.

## How It Works Today

- Camera ops set `context.WorldToCamera` / `CameraToClipSpace` only **for their subtree** and restore them afterwards
  (`Operators/Lib/Symbols/render/camera/Camera.cs:36-45`). A post effect runs after `[RenderTarget]` has returned, so
  that state is gone. Hence the explicit `CameraReference` input on SSL, GodRays, TXAA, MotionBlur (all `.t3`
  composites, which feed it into `_TransformsCBufferFromCam`).
- Camera writers (set the context matrices): `Camera`, `CameraWithRotation`, `OrbitCamera`, `OrthographicCamera`,
  `BlendCameras`, `ReuseCamera`, `ReuseCamera2`, `UseProjectorCam`, `RenderToCubeMap`, `RenderTarget` (the last two
  as render scopes).
- `ICameraPropertiesProvider` (`Core/Rendering/ICameraPropertiesProvider.cs`) is what consumers need: it has
  `CameraToClipSpace`, `WorldToCamera` and `LastObjectToWorld`. `BlendCameras`, `ActionCamera` and the four regular
  camera ops implement it.
- `[RenderTarget]` (`image/generate/basic/RenderTarget.cs`) has `ColorBuffer`, `DepthBuffer` and `NormalBuffer`
  outputs from one `Update` and pulls its scene at `:160` with `BypassCameras` forced off.
- `Texture2D` (`Core/DataTypes/Texture.cs:18`) is TiXL's own wrapper and already carries metadata
  (`CubeMapLayout`), so a texture can carry where it came from without a side table.

## Why Not "Last Camera on the Context"

The first idea: each camera writes itself into the context at the end of `Update()`, and consumers read that. It
breaks in four ways:

1. **Caching.** If the `[RenderTarget]` output is not dirty, no camera runs that frame and nothing is published.
2. **Several contexts.** The output window, thumbnails and Board previews each have their own `EvaluationContext`. A
   thumbnail can pull a cached texture whose camera only ever ran in another context.
3. **Pull order.** The consumer must read after pulling `Image`. Inside a composite that order depends on the
   inner graph.
4. **Camera overrides.** In `mesh → cam2 → cam1 → RenderTarget`, cam1 sets its matrices, cam2 overrides them and
   draws, and **cam1 finishes last**. "Last writer" picks cam1, but cam2 drew the image.

## Design

### 1. Recording: the innermost camera wins, scoped by the render target

- `EvaluationContext` gets `RenderedCamera` (`ICameraPropertiesProvider?`) and `RenderedCameraSerial` (`int`). These
  are plain fields, so nothing allocates.
- Every camera writer, after `Command.GetValue(context)`:
  ```csharp
  // before the pull
  var serialBefore = context.RenderedCameraSerial;
  ...pull subtree...
  // A camera inside our subtree already claimed the image: it overrode our matrices for the draws.
  if (context.RenderedCameraSerial == serialBefore)
  {
      context.RenderedCamera = this;
      context.RenderedCameraSerial++;
  }
  ```
  For `mesh → cam2 → cam1`: cam2 finishes first, sees no inner claim and records itself. cam1 then sees cam2's claim
  and leaves it. Result: cam2. Correct.
- `ReuseCamera` / `UseProjectorCam` record the camera they reuse, not themselves.
- `BlendCameras` records itself (it already implements `ICameraPropertiesProvider` with the blended matrices).
- `[RenderTarget]` and `[RenderToCubeMap]` save `RenderedCamera`, clear it before pulling the scene, read it
  afterwards and restore the outer value, so nested render targets (shadow maps, reflections) don't leak out.
- With `BypassCameras` (editor view camera) the camera ops skip themselves. That can't happen inside
  `[RenderTarget]`, which forces bypass off.

**Alternative considered: record at draw time.** Cameras set `context.ActiveCamera`, and whatever turns the context
matrices into GPU constants records it. This is exact for mixed scenes, but there are many such places:
`_TransformsCBuffer`, `_TransformsCBufferFromCam`, `_DispatchSceneDraws`, `_ProcessLayer2d`, `SliceViewPort`,
`GetScreenPos` and others read `context.WorldToCamera` directly. A single camera chain gives the same answer both
ways, so start with the camera-level rule and switch only if mixed scenes matter in practice.

### 2. Tagging: the texture remembers how it was rendered

- `Texture2D` gets `RenderSource`. Extend the existing `RenderTargetReference` (`Core/DataTypes/RenderTargetReference.cs`,
  already holds `ColorTexture`, `DepthTexture`, `NormalTexture`) with `Camera` (`ICameraPropertiesProvider?`) instead of
  adding a new class. Today `[RenderTarget]` only fills a reference you connect to its `TextureReference` input; it
  would additionally own one of its own for the tag. `[UseTextureReference]` keeps working unchanged. `[RenderTarget]` owns one instance per op and updates its fields after
  each render. That's an allocation per op, not per frame.
- `[RenderTarget]` sets `ColorBuffer.Value.RenderSource` to its instance. `Depth` / `Normal` are filled only when
  those buffers are enabled.
- This survives caching (a cached texture is the same object) and works across contexts. It also follows the real
  connection, so pull order doesn't matter.
- When `[RenderTarget]` recreates its textures (resize, format change), the new texture must get the tag again.

### 3. Resolving: one internal op inside each consumer

New `[_ResolveRenderSource]` in `render/postfx/_/`:

- Inputs: `Image`, `CameraReference`, `DepthMap`, `NormalMap`, wired to the composite's own inputs.
- Outputs: resolved `Camera` (Object), `Depth`, `Normal`.
- Order for each output: **connected value** (non-null), then **`Image.RenderSource`**, then nothing (status
  warning).
- Inside a composite, the inner op's inputs are always connected to the parent's inputs. So check for a **null
  value**, not `HasInputConnections`.
- Implements `IStatusProvider`: "Camera: auto from [Camera] 'Main'", or "No camera: connect CameraReference".
- SSL, GodRays, TXAA and MotionBlur get rewired through it. Each keeps its own `_TransformsCBufferFromCam`. Its
  `PreviousBuffer` only works with a single consumer, so the resolver must sit before it, not replace it.

### 4. Propagation through image filters (later)

`[Blur]` and friends produce new textures, so the tag is lost after the first filter:
`RenderTarget → Blur → SSL` finds nothing. Option: `_ImageFxShaderSetupStatic` (used by 48 symbols) copies
`RenderSource` from its input to its output. That's one central place, though `.cs` image ops won't be covered.
Measure how often post effects sit behind filters before doing this.

## Phases

1. **Record + tag.** `EvaluationContext` fields, the serial rule in the camera writers, scoping in `RenderTarget` /
   `RenderToCubeMap`, `Texture2D.RenderSource`. No user-visible change yet. Verify with the debug bridge: read the
   tag of a `RenderTarget` output for `mesh → cam2 → cam1 → RenderTarget` and confirm it is cam2.
2. **Resolve in `[ScreenSpaceLighting]`.** Add `_ResolveRenderSource`, rewire SSL, and check by eye against the
   explicitly wired version in `testing/PbrTests`.
3. **Roll out** to GodRays, TXAA and MotionBlur. TXAA and MotionBlur need previous-frame matrices: check that the
   auto camera keeps `PreviousBuffer` correct over several frames.
4. **UI.** The parameter window shows an unconnected `CameraReference` / `DepthMap` / `NormalMap` as
   "auto ← [Camera] 'Main'", with click-to-select. Add a test set under `.tests-manual/`.
5. **Optional:** propagation through `_ImageFxShaderSetupStatic`; switch to draw-time recording if mixed scenes need
   it.

## Motion Vectors

Temporal effects (TXAA, MotionBlur, reprojection) need to know where each pixel was last frame. TXAA may become the
default, so this is where the biggest visual gain is. It is also the most expensive part, so the cost must only exist
when a temporal effect asks for it.

### How It Works Today

- TXAA and MotionBlur reproject **from the camera alone**: rebuild the world position from depth, project it through
  the previous camera (`Assets/shaders/img/fx/TXAA.hlsl:93-111`). Correct for static geometry under a moving camera.
  Anything moving by itself (object transforms, deformation, moving points) gets the wrong history. TXAA hides it by
  rejecting history (`MotionThreshold`, previous depth in the history alpha), which shows as ghosting or as blur
  that stays.
- The previous camera comes from `_TransformsCBufferFromCam.PreviousBuffer`. It works with a single consumer only.
- **`_TransformsCBuffer` already keeps previous transforms** (`PrevBuffer` output, since `52f007feb`). It swaps
  current/previous once per `Playback.FrameCount` and keeps separate history per `__MotionBlurPass` index (used by
  `[RenderWithMotionBlur]` and `[RenderToCubeMap]`). 24 of the 26 draw ops in `mesh/draw` and `point/draw` contain one
  (all but `VisualizeUvMap` and `VisualizePoints`), but only `[DrawMovingPoints]` and `[MotionBlur]` connect
  `PrevBuffer`.
- `[DrawMovingPoints]` is the only per-object motion. `KeepPreviousPointBuffer`
  (`Symbols/point/usse/KeepPreviousPointBuffer.cs`) does a GPU `CopyResource` of the whole point buffer into a
  ping-pong pair. `DrawMotionPoints.hlsl:151-179` projects `PointsPrev[i]` through the previous matrices. It gates the
  swap with `HasTimeChanged`.

### Design: add motion vectors one layer at a time, with the camera-only estimate as the floor

1. **Velocity target on `[RenderTarget]`** (like `NormalBuffer`). Draws that know their motion write screen-space
   velocity. Every pixel without valid velocity falls back to the camera-only estimate. An op without support is never
   worse than today, so the ~40 draw ops can be migrated one at a time.
2. **Cost only when asked for.** `[RenderTarget]` sets `context.MotionVectorsRequested` while it renders its scene.
   Draw ops keep previous state and use the velocity shader variant only when that flag is set.
   - Temporal consumers set `RenderSource.VelocityRequested` on the image they read (via `_ResolveRenderSource`).
     `[RenderTarget]` sees it on the **next** frame. That costs nothing: a temporal effect has no history on its
     first frame anyway.
   - Thumbnails and Board previews never request it, so they stay cheap even when TXAA is the default for the main
     output.
   - `RenderSource` gets `Velocity` (`Texture2D?`), filled by `[RenderTarget]` when the target exists.
3. **Motion levels, from cheapest to most expensive:**

   | Level | What changes between frames | What has to be kept | Cost |
   |---|---|---|---|
   | Camera | the view | previous camera matrices (exist) | nothing new |
   | Rigid | `ObjectToWorld` per draw | one previous matrix per draw | close to nothing |
   | Moving points / instances | point positions | previous point buffer (`KeepPreviousPointBuffer`) | one GPU copy per draw op per frame |
   | Deformed mesh | vertex positions | previous vertex buffer | one GPU copy per deformed mesh per frame |

   Rigid covers most animated scenes (spinning or animated transforms) and needs no buffer copies.
   - The point copy could be reduced to position, rotation and scale instead of the full `Point` struct. That needs
     a compute pass instead of `CopyResource`, so only do it when profiling says so.
4. **Shared shader code.** One include computes current and previous clip position and outputs velocity, so draw
   shaders don't each implement it. Write velocity only when ZWrite is on, the same rule as depth. Transparent and
   additive draws don't write it.

### Motion Vector Risks

1. **Occlusion.** An op without velocity drawn in front of one with velocity must not leave the hidden object's
   velocity in the target. Proposal:
   - Store the depth at write time next to the velocity (RG = velocity, B = depth).
   - The consumer compares it with the depth buffer and falls back to the camera estimate on mismatch.
   - Ops without support must not write the target at all (per-target write mask, needs independent blend).
   - On Vulkan, a fragment shader that doesn't write a bound output leaves the value undefined. Check how
     `NormalBuffer` handles ops that don't write normals today (same problem) on both backends before choosing.
2. **Previous state per draw, not per frame.** An op drawn several times per frame (`[Loop]`, repeats, other
   contexts such as thumbnails) would swap its ping-pong buffers on every call, and "previous" becomes garbage.
   - Rotate at most once per frame, keyed on time like `DrawMovingPoints` does.
   - Inside `[Loop]`, key previous state by call index within the frame and reset when the count changes. Until
     that exists, draws inside loops write "unknown".
3. **Identity breaks.**
   - When the point count or topology changes, or time jumps (scrubbing, looping), there is no valid previous
     state: write "unknown".
   - Particles that get recycled jump in one frame: reuse `DrawMovingPoints`' `VelocityJumpThreshold`.
4. **Memory.** The velocity target is another full-resolution RT (RG16F + depth, or RGBA16F). Previous point and
   vertex buffers double those buffers' memory, but only while requested.

### Motion Vector Phases

M0 is independent and can ship first. M1 and later come after Phase 3 above; they need `RenderSource` and
`_ResolveRenderSource`.

- **M0. History helper.** Shared keyed history (see Research Findings, "One History Helper") in `_TransformsCBuffer` and
  `KeepPreviousPointBuffer`. Ships on its own: fixes `[DrawMovingPoints]` in `[Loop]`. Compatibility test:
  `[DrawMovingPoints]` inside `[RenderWithMotionBlur]` and `[RenderToCubeMap]` looks identical before and after.
- **M1. Plumbing.** Velocity target on `[RenderTarget]`, the request flag and its one-frame round trip, TXAA reading
  velocity with the camera-only fallback. No visual change expected: compare TXAA output before and after.
- **M2. Rigid motion.** Connect the existing `_TransformsCBuffer.PrevBuffer` inside `DrawMesh` and the
  `DrawMeshAtPoints` family, bind it as a cbuffer and output velocity from the shared include. No new C# per op.
  Fix the loop/context gap in its history first (Research Findings, item 2). Test: a spinning mesh under a static
  camera should lose its TXAA ghosting.
- **M3. Moving points.** Gate `KeepPreviousPointBuffer` on the request flag in the instanced draws (`DrawMeshAtPoints`,
  `DrawPoints`, `DrawBillboards`). Move `DrawMovingPoints` onto the shared include.
  - `[DrawMeshAtPoints]` done 2026-11-15: `KeepPreviousPointBuffer` (Keep always on, so a still frame gives zero
    motion) → `GetBufferComponents` → SRV t9 `PreviousPoints`; `PrevBuffer` → b7. The instance transform is one
    function (`PlaceAtPoint`) for both frames. Shader-graph resources moved to `/*{RESOURCES(t10)}*/`. A missing or
    shorter previous buffer (zero rotation) falls back to the current point.
    Bridge check: points rotated by `[TransformPoints]` under a static camera give a smooth velocity field; with the
    rotation stopped velocity is exactly 0. Not yet: a jump threshold for recycled points; the other instanced draws
    (`DrawMeshAtPoints2`, `DrawMeshChunksAtPoints`, `DrawPoints`, billboards).
  - Next (user idea 2026-11-15): copy previous points only when the render target has a velocity buffer.
    `[RenderTarget]` tells its draws (they run inside it, so no lag); `[PickBuffer]` evaluates only the picked input,
    so an unpicked `KeepPreviousPointBuffer` costs nothing. Done as Phase 1 of `Plan_SystemContextVariables.md`
    (typed `RenderPassState.VelocityRequested`), not as another string-named context variable.
- **M4. Deformed meshes.** Previous vertex buffer for mesh ops that deform on the GPU, only when needed.
- **M2b. [ScreenSpaceLighting] temporal passes.** SSL is a composite of `[ScreenSpaceAO]`, `[ScreenSpaceReflections]`
  and `[TXAA]`; all three accumulate history with camera-only reprojection (`ReprojectToPreviousUv` in
  `shared/screen-space-tracing.hlsl`). SSL gets a `VelocityMap` input, passed to all three.
  - One shared helper: where velocity is valid, previous uv = uv − velocity (jitter-free, so no jitter correction);
    elsewhere the camera reprojection as before.
  - The history distance checks (`expectedDistance` from the previous eye) assume static surfaces. For moving pixels
    compare distance from the camera instead, like TXAA.
  - Order: AO and the internal TXAA first; reflections after. Reflections need the velocity at the ray hit for the
    reflected object's history, not only the surface's.
  - `[SSAO]` (standalone) keeps no history and needs no velocity. Rule: every op that keeps history takes velocity.
  - Started 2026-11-15: `screen-space-tracing.hlsl` has `VelocityMap` at t5, `PreviousUvOfSurface` and
    `ExpectedPreviousDistance`; `[ScreenSpaceAO]`'s temporal pass uses them. SSL's `VelocityMap` input feeds its AO and
    its TXAA. Reflections still use the camera-only path (t5 unbound reads 0 = unknown).
    Wiring applied and built; bridge smoke test: no shader errors. The quality comparison was inconclusive in a
    synthetic scene (too little contact occlusion); to be judged in a real scene.
  - User test 2026-11-15 (A/B over the bridge, values restored with undo): velocity visibly cleans AO and TXAA. The
    dark wash at roughness 1 and the trails around points come from the reflections (present with velocity on and off).
  - Reflections (2026-11-15): the temporal pass uses `PreviousUvOfSurface` for the surface (a moving mirror carries
    its image) and reconstructs the ray hit from surface, normal and stored hit distance (without the trace's roughness
    jitter). If the hit moved, the clamp stays on and history is capped at 3 frames: no reprojection is possible
    without the hit uv. The trace pass is not touched: it binds only t0–t2, and textures bind in order.
  - Limits: reflected objects without velocity (points, until M3) still trail. Separate item: the dark wash of
    reflections at high roughness is a look issue of the reflection pass, not of motion.
- Related fixes 2026-11-15 (user reported wrong depth/normals from shaded points):
  - `DrawPointsShaded.hlsl`: sphere normal used `sqrt(1 - d²)` with d already squared; depth was the flat quad's;
    the shaded surface used `radius * 0.01` instead of the quad half size 0.05. Now a real sphere impostor writing
    `SV_DepthLessEqual` (SPIR-V: FragDepth, DepthReplacing, DepthLess).
  - Pitfall: `psOutput psMain(...) : SV_TARGET` makes slang ignore the struct's semantics and number outputs in
    order, so an `SV_Depth` member became color output 2 (the velocity slot). Removed in `DrawPointsShaded.hlsl` and
    `mesh-Draw.hlsl`; still present but harmless (color/normal only) in `mesh-DrawAtPoints.hlsl` and
    `mesh-CustomDraw-template.hlsl`. Any shader that adds SV_Target2 or SV_Depth must drop it.
- **M5. MotionBlur** reads the same velocity target. Velocity is displacement per *frame*, which is what TXAA needs
  (its history is one frame old), but it follows frame-duration jitter: motion blur must scale it by
  shutter time / `Playback.LastFrameDuration`, or blur length jumps with frame timing (seen 2026-11-15).

## Research Findings (2026-11-14)

Code reading; nothing run.

1. **Previous transforms exist per draw op.** See "How It Works Today" under Motion Vectors: `_TransformsCBuffer.PrevBuffer`
   is in 24 of 26 draw ops. M2 becomes shader wiring plus the shared velocity include, not per-op C#.
2. **Its history breaks when an op is evaluated more than once per frame.** The swap happens on the first call of a
   frame; every further call in that frame overwrites `Current`. The next frame's `Previous` is then whatever ran
   *last*:
   - Inside `[Loop]`, every iteration but the last gets the last iteration's previous matrix (wrong velocity).
     `[Loop]` does not set `__MotionBlurPass`, so iterations share one history.
   - Other contexts in the same frame (thumbnails, Board previews, a second output) can overwrite `Current` with
     their camera. Whether they run after the main output in one `FrameCount` needs checking in the editor.
   - Fix direction: key history by (pass, call index within that pass). Constraints:
     - **The counter lives inside `_TransformsCBuffer`.** `[Loop]` must not set `__MotionBlurPass`: that variable means
       "repeat of the same frame, don't advance state" and is checked by `HasTimeChanged`, `Damp`, `Spring`, `Ease`,
       `AudioReaction`. Setting it in every loop would change their behaviour in existing graphs.
     - **Count per pass.** `[RenderWithMotionBlur]` is a `[Loop]` (6 iterations) whose index variable *is*
       `__MotionBlurPass`; `[RenderToCubeMap]` writes `prevPass + faceIndex` (`RenderToCubeMap.cs:212`). With a
       per-pass counter both keep call index 0, so an op evaluated once per (frame, pass) behaves exactly as today.
       That is the compatibility test.
     - **Only contexts that keep history count.** Add a per-context flag (e.g. `EvaluationContext.KeepsHistory`, true
       for output windows and the player). Thumbnails, Board previews and variation canvases report previous = current
       (zero motion) and leave the history alone. Today they can already overwrite `Current` in the same frame.
     - **Report "unknown" instead of guessing** when the call count differs from the last frame, when the op skipped a
       frame (`LastUpdateFrame != FrameCount - 1`; today a re-enabled op gets a seconds-old previous matrix, a velocity
       spike), or above a cap of ~256 calls (a 10k-iteration loop would otherwise allocate 20k cbuffers). Release
       history slots when the count drops.
     - **Not detectable:** the same instance reached in a different order with the same call count (alternating
       branches). Accepted as rare.
   - Visible change: `[DrawMovingPoints]` inside `[Loop]` gets correct per-iteration trails instead of the last
     iteration's history. A fix, but existing projects will look different.
   - Existing bug: `[RenderToCubeMap]` inside `[RenderWithMotionBlur]` collides pass numbers (pass 1 face 0 = pass 0
     face 1) because the face index is added. Combine as `pass * 6 + face` instead. The other readers only test
     `> 0`, so that is safe. Separate small fix.
3. **The occlusion problem already exists for normals.** Only 15 of 26 pixel shaders under `Assets/shaders`
   write `SV_Target1`. Mesh draws, lines, ribbons, tubes and shaded points do; `DrawPoints`, billboards, sprites and
   `DrawMovingPoints` don't. `[RenderTarget]` binds the normal RTV for the whole scene, so an unlit point in
   front of a mesh leaves the mesh's normal (or an undefined value, depending on the backend) in its pixels. Velocity
   will behave the same, so one fix should cover both targets.
4. **`RenderTargetReference` is the natural tag type** (see Design §2).
5. **`ActionCamera` needs no recording** (Open Question 2).
6. **`Plan_CameraOverhaul.md`** is at Phase 1 slices 1–2 (editor-only navigation). Its operator model is Phase 2,
   so camera recording (Phase 1 here) can go into today's camera ops without waiting.

### Editor Tests (2026-11-14, by the user)

1. **Gain: confirmed.** TXAA leaves ghosting on a mesh moved by a transform. Rigid motion (M2) is the priority after
   the plumbing (M1).
2. **Undefined outputs: no garbage.** An unlit `DrawPoints` in front of a mesh leaves a normal-looking `NormalBuffer`,
   i.e. not writing keeps the earlier value. Backend still to note. For velocity this means a point in front of a
   moving mesh would inherit the mesh's velocity, so the depth channel for the occlusion check stays.
3. **`[DrawMovingPoints]` in `[Loop]`: broken, as predicted, with a second cause.**
   - Loop variable unused: the first iteration looks right, later ones show no movement.
   - Loop variable moving the points: the result looks wrong, because each iteration uses the previous iteration as
     its "previous" points.
   - Cause: besides the `_TransformsCBuffer` history (item 2 above), `KeepPreviousPointBuffer` flips its ping-pong
     pair on every call. Its `Keep` input comes from `[HasTimeChanged]` (mode `DidAdvancedWithMotionBlur`), which
     returns early on repeated calls within a frame and keeps `HasChanged = true` (`HasTimeChanged.cs:23-35`).
     It only reports false when `__MotionBlurPass > 0`, which `[Loop]` does not set. Inside `[RenderWithMotionBlur]`
     the gate works, so motion blur is fine; plain loops are not.

### Consequence: One History Helper

Matrices (`_TransformsCBuffer`) and point buffers (`KeepPreviousPointBuffer`) need the same history rules: key by
(pass, call index within that pass), only in contexts with `KeepsHistory`, "unknown" when the call count changes or a
frame was skipped. Build it once as a shared helper and use it in both. That also fixes `[DrawMovingPoints]` in loops
today, independent of motion vectors, so it is a good first, separately shippable step (**M0**).

- Point buffers need a **byte budget**, not only the ~256 call cap: 3 iterations × 1M points means 3 × 2 full point
  buffers. Above the budget, report "unknown" (previous = current).
- With the helper in place, `KeepPreviousPointBuffer` no longer needs the `HasTimeChanged` gate for correctness. Keep
  it for "don't advance when time is paused" if that still matters.

## Risks

- **Mixed scenes.** `[Group]` with `mesh A → cam2` and `mesh B` drawn by an outer cam1 into one target: the last
  claiming chain wins. There's no right answer here. The status line has to make the choice visible, and an
  explicit connection fixes it.
- **Stale references.** `RenderSource.Camera` holds an op instance. A deleted camera stays referenced until the
  `RenderTarget` renders again. Clear the tag when the camera's `Dispose` runs, or hold a `WeakReference`. Repo
  rule: prefer Guid references; an instance is needed here for the matrices, so document why.
- **Invalidation.** Moving the camera re-renders `RenderTarget`, so `Image` becomes dirty and the resolver
  re-runs through a normal connection. No implicit dependency for `Plan_ContextVariableInvalidation.md` to track.
- **Save files.** Rewiring the four `.t3` composites must not break existing projects that connect
  `CameraReference` explicitly. The connected value always wins, so it shouldn't.

## Overlap

- `Plan_CameraOverhaul.md`: if cameras are unified into one model, the serial rule goes there once instead of in ten
  ops. Do Phase 1 after (or with) that consolidation if it is close.
- `Plan_ContextVariableInvalidation.md`: no conflict (see Invalidation above). Both add fields to `EvaluationContext`.

## Open Questions

1. Motion vectors: see the section below. Open there: whether velocity needs the depth channel for the occlusion
   check or whether a per-target write mask alone is enough.
2. ~~Does `ActionCamera` write the context matrices?~~ No: it only reads `context.ObjectToWorld`
   (`ActionCamera.cs:25`) and is used through a reference. It needs no recording logic.
3. Is a context-level fallback ("last rendered camera in this context") worth keeping for images that went through
   untagged ops? It brings back the cross-context and caching issues above, so the default answer is no.
