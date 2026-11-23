# Plan: Cat Animation Demo (Locomotion, Foot IK, Simulation)

Status: **Draft, not started.**
Last update: 2026-11-23
Builds on: [Plan_GltfAnimation.md](Plan_GltfAnimation.md) (phases 0-5 implemented).

## Goal

A cat character, rigged with Rigify and animated in Blender, that walks, trots and gallops
along a spline in TiXL. Its feet plant without sliding and adapt to the ground. It can blend into a
simulated ragdoll in some sections, its tail, ears and whiskers react with physical secondary
motion, and after posing, the skinned mesh flows into the regular mesh effects pipeline.

The demo drives the features. Every op stays generic (any quadruped, and later bipeds).

## Pipeline

```
LoadGltfScene ─► LocomotionBlend ──► (pose, contacts, root) ─► PlaceOnPath ─► BendSpineAlongPath
     (clips)         ▲ speed/turn from path                                  │
                     │                                                      ▼
                     └─ AnalyzeClip (cached: stride, contacts)            PlantFeet (IK + locking)
                                                                            │
                                                     LookAtJoint (head stabilization)
                                                                            │
                                    SimulatePose (PBD: tail/ears, powered ragdoll)
                                                                            │
                                              PoseToSkinMatrices ─► SkinMesh ─► mesh FX ─► draw
whiskers:  root points ─► SkinPoints (head joint) ─► SimulateStrands (GPU verlet) ─► draw lines
```

## Key design decisions

1. **Stay on the `Point[]` pose convention.** Pose = one point per joint, joint-local TRS,
   `F1` = weight, `F2` = parent index. Each new feature is another pose op in the chain. Extra
   per-op data also travels as `Point[]`:
   - **Contacts**: one point per foot. Position = world target, F1 = contact weight (0 = swing,
     1 = planted), F2 = foot joint index.
   - **Root**: a single point. Position and Orientation = character placement in world space.
2. **Extract a shared `PoseMath` helper** (`Operators/Lib/Utils/`, internal). It provides
   local↔object FK, topological order and ancestor walks. `PoseToSkinMatrices` already contains
   this logic, and IK, foot locking and PBD all need object-space joints. One implementation,
   allocation-free with caller-owned buffers.
3. **Phase comes from distance, not time.** A cycle advances by `Δphase = Δs / strideLength`.
   Feet cannot slide by construction, and the phase is a function of path distance, so
   the timeline stays scrubbable where possible (see Phase 2 for the stride-change caveat).
4. **Root motion comes from the hips, not a root bone.** Rigify's `root` control bone is dropped
   by glTF "Deform Bones Only" export. Author cycles with the body travelling forward in Blender
   (not in place). Clip analysis takes the hips joint's horizontal displacement over one cycle as
   the stride, then strips that displacement so the cycle plays in place before `PlaceOnPath`
   applies the path motion.
5. **Clip analysis is cached in Lib, not stored in Core.** Stride, cycle duration and per-foot
   contact intervals are computed once per `SceneAnimClip` and cached in a
   `ConditionalWeakTable<SceneAnimClip, ClipLocomotionInfo>`. Core's `SceneSetup` stays unchanged.
6. **Feet are named, legs are described by joint names.** Inputs like `"DEF-foot.L"` and the chain
   joints are string inputs resolved by name, like `RetargetPose`. Finding by name is the
   documented feature here, so that is not an inferred link.
7. **Deterministic first, state only where physics needs it.** Locomotion, IK and foot locking
   are computed from (time, path distance) with no frame-to-frame state. `SimulatePose` and
   `SimulateStrands` are stateful and follow the sim conventions below.
8. **One PBD solver for ragdoll and secondary motion.** Joint particles, bone distance
   constraints, cone angle limits, ground collision, plus a per-joint *stiffness toward the
   animated pose*. Stiffness 1 = pure animation, 0 = limp ragdoll, in between = powered ragdoll.
   Tail, ears and full ragdoll differ only in their stiffness masks. No rigid-body engine.

### Stateful sim conventions (SimulatePose, SimulateStrands)

- Step only when the frame advances. **Never step on motion-blur subframes** or extra evaluations
  in the same frame (check `EvaluationContext.RenderPass`).
- Use a fixed internal timestep with substeps. The frame delta is accumulated, so results don't
  depend on the frame rate.
- A `Reset` input, plus auto-reset when time jumps backwards or by more than a threshold.
  After a reset, the sim starts from the current animated pose with zero velocity.
- An optional `WarmUpFrames` input (simulate N fixed steps on reset) so offline renders that start
  mid-section look settled.

## Blender / asset pipeline (user side, documented for the test set)

- Rig: Rigify animal meta-rig (cat), generated rig.
- glTF export: **Deform Bones Only** on. Check after import that the `DEF-` hierarchy is a
  connected tree (some Rigify versions parent `DEF-` bones via `ORG-`/`MCH-` bones that are
  dropped). `SampleGltfAnimation` at time 0 must equal the rest pose.
- Clips: cycles start and end on the same pose, and the body travels forward (decision 4). Each
  clip covers exactly one cycle.
- Initial clip set: `walk`, `trot`, `gallop`, `idle`. Later: `sit`, `jump`, `land`, `getup`
  (ragdoll recovery). Turn clips are optional; turning is procedural at first (spine bend).

## Phases

### Phase 0 - Foundation and verification
- Resolve the open items in Plan_GltfAnimation "Needs verification" with the cat rig, especially
  item 3 (quaternion multiplication order). IK will expose it immediately.
- Verify Rigify export: rest pose identity, hierarchy, no double node transform.
- Extract `PoseMath` from `PoseToSkinMatrices` (no behaviour change; the existing Fox test set
  must still pass).
- **Milestone:** cat walk cycle plays and skins correctly in place.

### Phase 1 - Clip analysis, root motion and path following
- `AnalyzeClip` helper (cached, decision 5): stride (hips horizontal displacement per cycle),
  cycle duration, average speed = stride / duration.
- `PlaceOnPath` (CPU op): spline points + `Distance` → root point, with position, orientation from
  the tangent, optional banking. Also outputs `Speed` and `TurnRate` (curvature × speed) for the
  next phase. Reuse the existing path sampling helpers if they fit (check the point/spline ops
  before writing new sampling code).
- Single-clip locomotion: phase = `Distance / stride`, pose sampled at `phase × duration` with root
  motion stripped, then placed by the root.
- **Milestone:** cat walks along a spline with keyframed distance and no visible foot sliding on
  flat ground.

### Phase 2 - Locomotion blend (gaits)
- `LocomotionBlend` (CPU op): a gait set (clip indices + their analysis), speed → gait selection
  with thresholds and hysteresis, plus an optional manual gait override for artistic control.
- **Within a gait:** blend variants (e.g. straight / turn-left / turn-right from `TurnRate`) on a
  shared normalized phase. Clips must share the same footfall order.
- **Between gaits:** short crossfade (default 0.25 s). The target gait's phase is chosen so
  its foot contacts best match the current gait's contacts at the switch.
- Outputs: pose, contacts (per-foot weight for the current phase), current gait and phase.
- **Stride-change caveat:** when the effective stride changes, `phase = s / stride` jumps.
  The correct form integrates `Δs / stride(v)`. Spike first: can the op re-evaluate its `Distance`
  input at earlier times (like the existing time-shifting ops) to rebuild the integral after a
  scrub? If not, phase is integrated statefully and rebuilt from the section start on jumps.
- **Milestone:** cat accelerates from walk to gallop along a spline with clean transitions.

### Phase 3 - Foot contacts, IK and anchoring
- Contact detection in `AnalyzeClip`: a foot counts as planted where its world-space velocity
  (in-place velocity + root velocity) and its height are both near zero. Optional per-clip
  override string (e.g. `"foot.L: 0.10-0.45"`) if detection is wrong.
- `PlantFeet` (CPU op):
  - **Locking without state:** the planted world position of a foot is the foot position at
    contact start. Contact start is at `s_start = s - (phase - phaseStart) × stride`, so it is
    computed by evaluating the pose and path at that distance. That makes it deterministic and
    scrubbable.
  - **Front legs:** analytic two-bone IK (upper arm, forearm) to the wrist target; the scapula
    keeps its animated rotation.
  - **Hind legs (digitigrade):** keep the animated metatarsus orientation, so ankle = target −
    animated metatarsal vector, then two-bone IK (femur, tibia) to the ankle.
  - Pole direction comes from the animated knee/elbow so knees don't flip.
  - **Ground:** flat plane with height first; later a heightfield or mesh raycast. Feet in swing
    get a ground-height offset blended by contact weight.
  - **Pelvis/shoulder adjustment:** lower the hips or chest when a leg can't reach its target
    (slopes, steps).
  - Blend between animated and IK pose per foot by contact weight, with a global `Amount`.
- **Milestone:** cat walks over a sloped plane and steps; planted feet stay fixed in world space.

### Phase 4 - Body polish
- `BendSpineAlongPath`: spine joints follow the path like a train (joint *i* sits at distance
  `s - offset_i`), so the body curves through turns. Additive on top of the cycle.
- `LookAtJoint`: aim a joint chain (neck, head) at a target with per-joint weight. A
  "stabilize" mode counter-rotates the head against body motion (typical cat look).
- **Milestone:** cat turns smoothly and its head holds steady while walking.

### Phase 5 - Whiskers (GPU)
- `SimulateStrands` (GPU compute op): strand roots as a point buffer (separator-split strands),
  segment count and length, bend stiffness, damping, gravity, and inherited root velocity.
  Output is a point buffer for line/ribbon drawing and effects. Check whether the constraint
  pass in `VerletRibbonForce` / its shader can be shared.
- Roots: points authored in head-joint space in TiXL, skinned with `SkinPoints` bound to the
  head joint. Later: import whisker roots from a named glTF mesh.
- Stateful: follows the sim conventions above. Independent of phases 1-4, can start any time.
- **Milestone:** whiskers sway and settle when the head turns.

### Phase 6 - Pose simulation (secondary motion, then ragdoll)
- `SimulatePose` (CPU op): input pose + skeleton, per-joint stiffness mask (`Point[]` F1, same
  convention as the `BlendPoses` weight mask), gravity, damping, substeps, ground plane with
  friction, and optional collision spheres/capsules later.
- Particles at joints (plus child tips for end bones). The result converts back to joint-local
  rotations: bone direction from particles, twist kept from the animated pose.
- **Secondary first:** tail and ears with high stiffness at the root falling off to the tip.
- **Ragdoll:** an animated `RagdollAmount` lowers the stiffness globally. Entering uses the
  previous frame's animated pose to initialize velocities, so the cat keeps its momentum.
  Getting back up is a crossfade into an authored `getup` clip, matched by body orientation.
- Angle limits per joint type (spine, hip, shoulder, knee) as presets so the user doesn't
  have to tune per joint.
- **Milestone:** cat jumps, goes limp mid-air, lands as a ragdoll, then gets up.

### Phase 7 - Mesh effects and wrap-up
- Effects work already (the `SkinMesh` output is regular `MeshBuffers`). Check which effects need
  previous-frame positions (motion-based deformation, velocity for motion blur). Don't share a
  transforms cbuffer across passes, or the previous buffer silently equals the current one.
- Decide whether morph targets (blinks, ear flicks) are needed or whether bones are enough.
- `.help/docs/using/CharacterAnimation.md`: extend with locomotion, IK and simulation.
- `.tests-manual/cat-locomotion.md`: new test set (Rigify export checklist, path walk, gait
  change, slope, ragdoll section). Link it here instead of duplicating steps.
- Sweep transitional comments; feature retrospective (overlaps with particle sims, path ops).

## New operators (proposed)

| Op | Kind | Phase |
|---|---|---|
| `PlaceOnPath` | CPU | 1 |
| `LocomotionBlend` | CPU | 1-2 |
| `PlantFeet` | CPU | 3 |
| `BendSpineAlongPath` | CPU | 4 |
| `LookAtJoint` | CPU | 4 |
| `SimulateStrands` | GPU | 5 |
| `SimulatePose` | CPU | 6 |

Helpers in `Operators/Lib/Utils/`: `PoseMath`, `ClipLocomotionInfo` (+ cache), `TwoBoneIk`.
Folder: the existing pose ops stay in `render/skinning`; the new ops go to `render/character`.

## Overlap with other plans

- **Plan_SceneDocument** would replace `SceneSetup` as the skeleton and clip carrier. The clip
  analysis cache (decision 5) keys on the clip object, so it moves to the document's clip type
  with no other change. If that plan lands first, glTF `Extras` (Blender custom properties) are
  a better home for per-clip contact overrides and gait tags than op string inputs.
- **Plan_GltfAnimation** Phase 5 / verification items are Phase 0 here.

## Open questions

1. **Live or offline?** Offline rendering with scrubbing makes determinism (Phase 2 spike,
   sim warm-up) more important; for live playback the stateful fallbacks are fine.
2. **How is the path traversal authored?** Keyframed distance (recommended: exact control over
   timing) or keyframed speed (more natural acceleration)? Either way, `PlaceOnPath` takes one;
   the other can be derived later.
3. **Gait control:** fully automatic from speed, or keyed gait override as the main tool with
   automatic as fallback?
4. **Ground:** is a flat plane plus slopes enough for the demo, or does the cat walk on
   arbitrary meshes (needs raycasts against mesh buffers on the CPU)?
5. **Morph targets** for the face: needed, or bones only?

## Deferred

Two-way collision between the cat and the scene, multiple characters, GPU locomotion for crowds,
full-body IK (paws on objects), automatic turn-clip generation, motion matching.
