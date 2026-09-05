# Plan: glTF Animation Playback & Skinning

Status: **Phases 0-4 implemented and verified in the editor (animated Fox.gltf plays
through the full sample -> matrices -> skin -> draw chain). Phase 5 implemented,
awaiting editor test. Wrap-up pending.**
Last update: 2026-09-02

## Goal

Load rigged/animated glTF models, play their animation clips, blend and retarget poses,
and (later) bind skeletons to arbitrary procedural meshes - all composable with the
existing mesh/point effects pipeline.

## Key design decision: no new connection types

Instead of dedicated `Skeleton` / `Pose` / `SkinWeights` graph types, everything maps
onto existing types:

- **Pose** = `Point[]` - one point per joint with joint-local TRS in
  Position/Orientation/Scale. `F1` = 1 (width semantic), `F2` = parent joint index.
- **Skeleton + animation clips** = extended `SceneSetup` (`SceneSkeleton`,
  `SceneAnimClip` / `JointAnimChannel` in `Core/DataTypes/SceneSetup.cs`).
  Clip curves are SharpGLTF `ICurveSampler<T>` created once at load with isolated
  memory (handles STEP / LINEAR / CUBICSPLINE incl. quaternions); the `ModelRoot`
  is not kept alive.
- **Skin weights / skin matrices** = plain `BufferWithViews`
  (weights: int4 joints + float4 weights, 32-byte stride; matrices: 64-byte stride,
  System.Numerics row-vector layout, applied as explicit float4 rows in HLSL).

Convention: pose point buffers are **joint-local space**; local->object happens in
`PoseToSkinMatrices`. Skinned output is bind-pose/object space - draw it with a plain
mesh-draw op (glTF spec: skinned meshes ignore their node transform).

## Phase status

### Phase 0 - Pre-code checks ✅
- Vertices copied 1:1 from accessors per primitive -> side buffers indexable by vertex id.
- Vertices stored mesh-local; node transform applied at draw (dispatches).
- No per-vertex attribute mechanism; side buffer confirmed as right approach.
- `ClipTiming` doesn't exist -> ops use `context.LocalFxTime` (bars!) / `OverrideTime`.
- SharpGLTF.Core 1.0.6 (referenced by Core and Lib); Runtime/Animations included.

### Phase 1 - Loader emits skeleton + weights ✅
`LoadGltfScene`: reads `Node.Skin`, dedupes skeletons per skin, emits
`SkinWeights` (per selected dispatch, normalized, JOINTS_0/WEIGHTS_0) and
`SkeletonPoints` (object-space rest pose for DrawPoints viz) outputs.
Not supported: skins in the `CombineBuffer` chunk path (path is WIP anyway).

### Phase 2 - Skinning path ✅
- `PoseToSkinMatrices` (CPU): topological joint order (glTF order not guaranteed,
  cached per skeleton, cycle fallback), ancestor walk, inverse-bind x object pose,
  uploads matrix buffer. No pose connected -> rest pose -> identity skinning.
- `SkinMesh` (GPU, code-only op): LBS of position + full TBN in
  `Lib:shaders/cs/SkinMeshVertices-cs.hlsl`; output is a regular `MeshBuffers`
  (own vertex buffer, shared index/chunk buffers) -> pipes into the effects pipeline.
  Pass-through with status warning on missing/mismatched inputs.

### Phase 3 - Animation ✅
- Loader: `ExtractAnimationClips` -> per-joint T/R/S curve samplers; rigid
  (non-joint) node animation channels are skipped for now.
- `SampleGltfAnimation`: clip index + skeleton index + time -> `Point[]` pose.
  Rest pose as base each frame, quaternions normalized, Loop (wrap) or clamp.
  Time: context bars -> seconds via `bars * 240 / BPM`; connected `OverrideTime`
  is seconds. Output has `DirtyFlagTrigger.Animated`.

### Phase 4 - Pose operators ✅
`BlendPoses` (lerp/slerp + optional per-joint F1 weight mask), `AdditivePose`
(rest-pose-relative deltas, weighted), `OverrideJoint` (local-frame offsets from
graph inputs), `RetargetPose` (case-insensitive name map, rest-delta rotations,
opt-in `TranslationScale` for root motion, cached mapping).
All CPU ops on `Point[]`, allocation-free per frame.

### Phase 5 - Procedural binding ✅ implemented (untested in editor)
- `SkeletonFromPoints` (CPU): `StructuredList` points (via [PointsToCPU]) -> `SceneSetup`
  with one skeleton. Chain mode (parent = previous, NaN-scale separators split chains)
  or `UseF2AsParent` (F2 = parent point index). Feed a *static snapshot* for the rest pose.
- `PoseFromPoints` (CPU, added beyond original plan): object-space points (same source,
  continuously updated) -> joint-local pose. This is how a procedural rig animates:
  rest snapshot -> SkeletonFromPoints; live points -> PoseFromPoints -> PoseToSkinMatrices.
- `BindToSkeleton` (GPU): envelope weights on the rest pose; accepts a Mesh *or* a point
  buffer (two shaders, shared `shared/skin-binding.hlsl`). Bone segments joint->avg(children);
  Radius/FalloffPower/MaxInfluences(1-4); outside all envelopes snaps to nearest bone.
- `SkinPoints` (GPU): point counterpart of SkinMesh; rotates Position and Rotation by the
  blended matrix (orthonormalized -> quat); separators/unbound points pass through.
- Milestone graph (untested): spline -> PointsToCPU(snapshot) -> SkeletonFromPoints;
  animated spline -> PointsToCPU(continuous) -> PoseFromPoints -> PoseToSkinMatrices;
  mesh -> BindToSkeleton -> SkinMesh -> draw.

### Wrap-up (in progress)
- ✅ `.help/docs/using/CharacterAnimation.md` (drafted, review after Phase 5 test)
- ✅ `.tests-manual/gltf-character-animation.md` (drafted; assumes Fox.glb)
- ⬜ Sweep transitional comments; feature retrospective

## Needs verification (first editor test on a rigged asset, e.g. Fox.glb / CesiumMan.glb)

1. **Rest-pose identity**: `SkinMesh` without pose must render identical to the raw mesh.
2. **Bars vs seconds**: clip speed correct at non-120 BPM; `SampleGltfAnimation` at
   time 0 == rest-pose render.
3. **Quaternion multiplication order** (System.Numerics trap): `OverrideJoint` with a
   single-axis offset on a knee/elbow must hinge around the *bone's* local axis.
   If it swings around a parent/world axis instead: flip `rest * delta` -> `delta * rest`
   in `AdditivePose`, `RetargetPose`, `OverrideJoint` (one line each).
4. **Node-transform offset**: a model whose skin sits under a transformed node must not
   be double-transformed (skinned draw must bypass the node transform).
5. New `LoadGltfScene` outputs + `render/skinning` symbols appear after editor restart
   (hand-authored .t3/.t3ui files). ✅ (Phases 1-4 ops confirmed; Phase 5 ops pending)
6. Phase 5: `BindToSkeleton` on the Fox mesh + its own skeleton should roughly reproduce
   the authored deformation (envelope quality check). Then the spline-chain milestone.
7. Phase 5: `SkinPoints` orientation convention (qFromMatrix3Precise/qMul order in
   `SkinPoints-cs.hlsl`) - check with visibly oriented instances on a rotating joint;
   if instances counter-rotate, swap the qMul argument order.

## Fixed along the way

- Loader tangent generation was broken (wrote only vertex `a`, ignored authored
  `TANGENT` attribute). Now: authored tangents used when present (w = handedness);
  fallback accumulates per-triangle tangents on all corners + Gram-Schmidt.
  Expect (improved) shading changes on normal-mapped glTF assets.
- `SceneSetup.Dispose` inverted guard (fixed in separate session/task).
- Loader generated no normals when the `NORMAL` attribute is absent (e.g. Khronos
  Fox.gltf) - every vertex got `Up`, rendering as a flat silhouette. Now computes
  area-weighted smooth normals from the triangles, before the tangent pass.

## Deferred

Morph targets, `JOINTS_1` (>4 influences), GPU keyframe sampling for instanced
crowds + vertex-shader skinning draw op (pairs with instancing), IK,
dual-quaternion skinning (only if LBS artifacts show up), rigid node animation,
skins in the CombineBuffer chunk path.
