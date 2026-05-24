# Notes on Timeline Development (2026-04-03)

## Goals

- Improve interpolation correctness (especially smooth/spline tangent behavior)
- Reduce runtime/editor overhead in timeline rendering and interaction
- Improve serialization quality while keeping backward compatibility
- Prepare architecture for future curve features (for example, a speed-curve editor and shared rendering between dope sheet and curve editor)

## Animation Curve Interpolation

Current issues:
- Interpolation is sometimes incorrect (for example, smooth tangent weighting can degrade segments that should remain straight)
- Performance is uneven; some paths allocate excessively
- Serialization is verbose and not precision-aware

Example of the current key payload:

```json
{
  "Time": 50.443,
  "Value": -0.30000001192092896,
  "InType": "Spline",
  "OutType": "Spline",
  "InEditMode": "Smooth",
  "OutEditMode": "Smooth",
  "InTangentAngle": 6.283185307179586,
  "OutTangentAngle": 3.141592653589793
}
```

Observed model issues:
- Interpolation behavior and UI edit mode are mixed in one concept
- Tangent angle storage likely does not require full `double` precision
- Value serialization often includes unnecessary float-to-double tails

### Proposed key model (concept)

```csharp
struct Key
{
    double U;
    double Value;

    [Flags]
    enum Flags
    {
        BreakTangents,
        SaveDoublePrecision,
    }

    enum Interpolation
    {
        Linear,
        AutoSmooth,
        Smooth,
        Horizontal,
        ConstantOut,
    }

    float AngleIn; // Candidate: replace with normalized derivative/slope representation.
    float AngleOut;
    float TensionIn = 1;
    float TensionOut = 1;

    Interpolation InType;
    Interpolation OutType;

    int UniqueId; // Runtime-stable id, not serialized.
}
```

Notes:
- Backward-compatible deserialization is mandatory
- To reduce file size, omit default values where possible (for example: linear mode, tension = 1)
- It would be useful to find a tangent representation that avoids tangent recomputation when changing the `U` of linear keyframes
- The format should stay extensible toward After Effects-like keyframe/speed workflows
- At runtime, each new key should receive a globally unique, stable id (atomic static counter) for robust selection and interaction handling

## Curves

- Curves should include a revision integer for caching and possible incremental backup serialization
- On every modification of a curve or any key in that curve, increment the curve revision (or synchronize to symbol revision if that becomes the chosen policy)

## Animation Curve Rendering

Curve rendering should be rewritten because:
- It should avoid per-frame allocations
- It should be optimized for immediate-mode UI drawing
- It should support adaptive sparse caching

Overall idea:
- Treat each curve as three visual ranges: pre, body, post
- Draw each range with a single `DrawList.AddPolyline()`
- Compute sampled values once per visible range and cache them
- Use a sparse cache of `Vec2<U, Value>` samples
- Keep cache independent from keyframe-only sources so procedural animation can share it

Potential draw flow:
- Draw
  - each `AnimationParameter`
    - each vector component
      - `GetOrUpdateValueCacheForVisibleRange`
        - Linear: one segment, omit points on straight lines
        - Constant/discrete: allow two points at same `U` where required
        - Non-linear:
          - fixed step count (for example, 20 steps) for dope-sheet preview, or
          - zoom-dependent step count (for example, ~5 px target spacing)
          - optional extra points around extrema/sign changes for procedural curves with sharp corners (for example, `frac(u)`)
      - Iterate cached values and build the draw list point stream
        - Skip before visible range start
        - Add last point and stop after visible range end
        - Update min/max ranges for next-frame value normalization
        - Skip points if pixel delta is too small (for example, < 5 px)
      - Draw polyline with style variations for pre/body/post

Side notes:
- `AnimationParameter` should keep last-frame min/max and damped display range
- Clamp visible vertical range before adding draw points (instead of relying on expensive clip-rect usage)
- Cached values should remain precise at keyframes
- Lazy cache refinement after zooming is optional; likely not required initially
- Cache invalidation should include curve revision
- Cache strategy is open; likely a fixed-size pool with LRU-like reuse
- Planned feature: show an animation parameter simultaneously in dope sheet and time-synced curve editor using the same cache segments and draw algorithm

## Advanced Curve Interactions and Tension

New interaction idea for motion design:
- Use a Gain/Bias remap interaction over normalized `[0..1]` time
- Vertical dragging adjusts concentration/contrast of timing distribution
- Horizontal dragging shifts temporal bias toward start or end
- Reuse existing Gain/Bias methods where possible (`Core/MathUtils.cs`)

Manipulation scopes:
- Between two keyframes
- Within bounds of selected keyframes
- Within a timeline work range

### Example: Two Linear Keyframes

Start interaction with a modifier and drag between two linear keys:
- Drag down: concentrate toward center defined by mouse `x` at drag start
  - Adjust tangents so interpolation becomes snappier (more vertical)
- Drag up: concentrate toward ends (ease in/out)
  - Adjust tangents so interpolation becomes smoother (more horizontal)
- Drag left/right: rebalance timing toward start or end
  - Intuition example: for `[0,0] -> [1,1]`, moving focus from `U=0.5` toward `U=0.75` should shape tangents so the curve passes smoothly near `[0.75, 0.5]`
- Mathematical precision is not the priority; artistic control is

### Example: Multi-Keyframe Selection

Select many keyframes across several parameters (for example, `U=10` to `U=50`):
- Drag modifier up to concentrate keys toward center and spread them at the boundaries
- Outermost keys should keep their original `U`
- Interpolation type should usually remain unchanged (for example, linear stays linear)
- For non-linear curves, preserve perceived smoothness as much as possible

## Decisions After Clarification

- `UniqueId` stays runtime-only and is not serialized
- Key identity only needs to be unique per process session; resetting the counter on restart is acceptable
- Separating interpolation behavior from edit mode should happen early and be independent of rendering refactoring
- Tangent angles can use `float` precision for both storage and computation
- Default-value omission in JSON can be enabled immediately, scoped to curve/animation serialization
- Introduce an explicit curve-format version field; if missing, treat payload as legacy format and convert during read
- Cache correctness must depend on `CurveRevision`; mapping-mode changes must also increment curve revision
- Visible range should drive cache extension/update decisions, not global invalidation
- Initial cache budget target: roughly 1000-5000 cached curve-vector ranges, capped around 16 MB
- Gain/Bias behavior should be context-dependent:
  - Two-key interaction can update tangents/interpolation behavior
  - Multi-key interaction should preserve linear/constant intent and only adjust tangents where non-linear behavior exists

### Interpolation Semantics Guardrails

- Primary model: value-space interpolation in `(time, value)` remains the core authored curve model
- Temporal easing/retiming is optional and must not replace value-space interpolation
- Segment evaluation modes:
  - Mode A: direct value-space interpolation (hold/linear/spline)
  - Mode B: optional retime layer (`u -> s`) followed by value-space sampling
- Equal-endpoint behavior must stay explicit:
  - Value-space splines may produce motion/overshoot with equal endpoint values
  - Timing-only remap over linear interpolation must not create motion when endpoints are equal
- AE compatibility target remains representational compatibility, not a strict one-to-one UI clone

### Open Design Questions

- Bulk keyframe moves are slow (~100ms for 200 keys in Debug): `MoveKey` calls `UpdateTangents` after every single key. Fix: add a batch move API that defers tangent recomputation until all keys are moved, then recomputes once.
- Should `UniqueId` remain `int` or switch to `long` to avoid extremely long-session overflow concerns?
- What is the final `IRevisionVersioning` API shape (`FlagChanged`, `BeginNewFrame`, `WasChangedInLastFrame`, `Revision`)?
- Should curves serialize the symbol revision of their last modification, or should that remain runtime-only metadata?
- Preferred naming/location for legacy migration helper (for example `ReadKeyframeFromJsonFormat` in a dedicated serializer class)?
- Tangent storage choice for AE-like behavior:
  - Store explicit `(time, value)` handles, or
  - Store semantic speed/influence and derive handles at evaluation/edit time
- If unit cubic temporal bezier is added later, what monotonicity and inversion constraints should be enforced?
- Should VDefinition become effectively read-only externally, with all mutations routed through `Curve.UpdateKey()`? This would guarantee ChangeCount bumps, tangent recomputation, and cache invalidation without requiring ParentCurve back-references. Large refactor touching CurvePoint, CurveEditing, ChangeKeyframesCommand, Animator, etc.

## Incremental Implementation Plan

### Milestone 1: Correctness and Safety Baseline

- Fix key metadata copying issues (`Weighted`, `BrokenTangents`) in clone/copy paths
- Fix duplicate change-count increments in curve update helpers
- Add regression tests for clone/copy fidelity and change-count behavior
- Keep runtime behavior otherwise unchanged

Exit criteria:
- No known metadata loss in key copy/clone operations
- Existing projects load and animate exactly as before

### Milestone 2: Key Identity, Revision, and Model Separation

- Add runtime key id assignment (`UniqueId`) using an atomic static counter (session-scoped, not serialized)
- Add per-curve revision field and increment on every key/time/value/tangent/mapping change
- Separate interpolation behavior from edit mode in the key model early in the rollout
- Keep selection and editing code compatible with old object-reference paths during transition

Exit criteria:
- Key ids are stable during edits and drags
- Revisions increment deterministically for all editing operations
- Interpolation/edit-mode separation is implemented without regressions

### Milestone 3: Serialization Evolution (Backward Compatible)

- Add explicit curve-format version metadata in serialized animation data
- Implement legacy fallback: if version is missing, read old payload and convert to new model on load
- Add a dedicated migration/read helper (for example `ReadKeyframeFromJsonFormat`)
- Enable default-value omission for new curve-format writes
- Add round-trip tests for old and new payload variants

Exit criteria:
- Old projects load unchanged
- New saves round-trip without data loss
- Legacy payloads are transparently migrated at read time

### Milestone 4: Value-Space Evaluation Baseline

- Keep value-space interpolation as the primary model (hold, linear, spline/Hermite)
- Refactor interpolation key lookup and evaluation flow to reduce allocations and temporary objects
- Revisit tangent recomputation triggers; avoid unnecessary recompute for strictly linear/constant paths
- Add interpolation tests for constant, linear, spline, and boundary behavior
- Add explicit test cases for equal-endpoint segments with non-zero tangents

Exit criteria:
- Correct sampled values across reference scenarios
- No new per-frame allocations in evaluation hot paths
- Equal-endpoint behavior is intentional and covered by tests

### Milestone 5: Dope-Sheet Rendering Cleanup (No Cache Yet)

- Remove LINQ and avoid `ToArray()`/`ToList()` in hot rendering paths
- Introduce reusable buffers for polyline points
- Fix min/max damping write-back so normalized display behaves correctly

Exit criteria:
- Reduced GC pressure while scrubbing/panning
- Visual output remains equivalent to baseline

### Milestone 6: Sparse Visible-Range Cache (Phase 1)

- Implement cache entries keyed by revision + zoom bucket (+ mapping mode if needed)
- Treat visible range as a cache scope extension/update decision, not a global invalidation trigger
- Build pre/body/post polyline segments from cached samples
- Add bounded cache size and LRU-like eviction policy (target budget around 16 MB)

Exit criteria:
- Cache invalidation is correct during edits
- Cache scope extension behaves correctly while panning/zooming
- Improved frame time in dense animation scenes

### Milestone 7: Shared Cache for Dope Sheet and Curve Editor (Phase 2)

- Unify sample/cache pipeline so both views consume the same value cache
- Keep per-view styling separate while sharing sample generation
- Add consistency tests/checks between views

Exit criteria:
- Matching curve shapes across both views
- Stable interaction performance with both views active

### Milestone 8: Temporal Retime Layer (Optional)

- Implement optional time-remap interaction over selected ranges without replacing value-space interpolation
- Start with Schlick gain/bias for retime controls
- Preserve boundary keys and integrate with undo/redo
- Two-key mode: allow tangent/interpolation shaping
- Multi-key mode: preserve linear/constant intent and adjust only non-linear tangent behavior

Exit criteria:
- Usable artistic pacing control for two-key and multi-key scenarios
- Value-shape editing remains independent from optional retime controls
- No destructive behavior in common editing workflows

### Milestone 9: AE-Compatible Tangent Representation and Hardening

- Decide and implement storage model for AE-compatible tangents (handle-based or speed/influence-based)
- Add conversion/derivation path so value graph and speed graph are consistent views of the same segment data
- Add stress/performance validation on large timelines
- Document migration/compatibility behavior
- Finalize schema/version policy if still pending

Exit criteria:
- Stable editing/playback behavior on production-scale files
- Documented AE-compatibility semantics and representation limits
- Clear migration and fallback documentation
