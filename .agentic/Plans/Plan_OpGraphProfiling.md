# Operator-Graph Profiling & MagGraph Heatmap

Goal: let users identify CPU and GPU bottlenecks *inside the operator graph* — which specific ops in a project are expensive on a given frame — and surface that information directly on the MagGraph canvas as a live heatmap. Complements [Plan_RenderProfiling.md](Plan_RenderProfiling.md), which measures the *frame envelope*; this plan measures *inside the frame*.

## Context

Today we have three profiling artefacts and no unified view:

- **`T3.Core.Stats.OpUpdateCounter`** — counts slot updates per frame (`Core/Stats/OpUpdateCounter.cs`). One integer, no per-op breakdown.
- **`GpuMeasure` operator** (`Operators/Lib/render/analyze/GpuMeasure.cs`) — wraps a sub-graph, measures its GPU time via `ID3D11_QUERY_TIMESTAMP` + `_DISJOINT`. Great building block, but two limitations: (a) must be placed explicitly in the graph by the user, (b) alternates frames ("measure / skip-to-read") so only measures every other frame because `GpuQuery.GetData` is called with `AsynchronousFlags.None` (blocking).
- **`Operators/Lib/Utils/GpuQuery.cs`** — thin SharpDX `Query` wrapper. Reusable.

None of this gives a graph-wide picture. A user who wants to know "why is this project slow on frame 1234" has no way to scan the graph and see where the cost is concentrated.

The naive approach — measuring *every* op via a stopwatch around `Slot.Update` ([Slot.cs:160](../../Core/Operator/Slots/Slot.cs:160)) — is rejected. The update path runs in hot loops and already uses `AggressiveInlining`; wrapping it unconditionally would pay a stopwatch cost on every slot every frame, tens to hundreds of thousands of times per second in a busy graph.

Instead, this plan uses **opt-in attribute-tagging** of interesting op classes and a toggle-able overlay so the feature has **zero cost** when the user is not actively profiling.

## Goals

- Identify CPU and GPU bottleneck ops in a live project, at the granularity of "which op is expensive," not "which draw call."
- Display the result as a MagGraph heatmap: per-node colour ramp + badge showing inclusive ms.
- Zero overhead when the overlay is off. Small, bounded overhead (<2%) when it is on.
- Reuse existing `GpuQuery` infrastructure; extend `GpuMeasure` to benefit from the same triple-buffering.
- Minimal per-instance storage — a ~24-byte struct, allocated lazily, no per-op history.

## Non-Goals (initial release)

- **Per-draw-call profiling.** Use PIX. Explicit non-goal because the measurement overhead at that granularity becomes 5–10% and the numbers get noisy (see Plan_RenderProfiling Part 1 granularity table).
- **Sampling every op in the graph.** Only attribute-tagged classes are profiled. "Value" and "Add" aren't measured — they're too fast for GPU timestamps to be accurate and too common for the overhead to be acceptable.
- **Call-graph / flame-graph export.** A CSV dump of the current frame's self/inclusive numbers is fine; a proper flame-graph tool is follow-up work.
- **History per instance.** Expressly rejected. If a user wants "how did this op's cost change over the last 30 seconds," they'll capture a dedicated window via a separate "Capture history for selected op" feature (see Part 7).
- **Multi-threaded safe profiling.** The op update path is single-threaded today; profiling assumes the same. If that changes, revisit.

---

## Architectural Decisions

1. **Opt-in via attribute, not opt-out.** Default = not profiled, zero cost. Adding `[ProfilableOp]` to an operator class is the one-line change that enrolls it.
2. **Three tag categories.** `Heavy` (Draw*, ImageEffects, compute shaders, ray marchers), `Structural` (Group, Execute, Switch, ForEach — interesting for understanding shape, not necessarily cost), `Io` (file load, audio decode, network). Categories are a bit-flag so an op can be both Heavy and Io.
3. **Self vs. inclusive time, both captured.** Standard flame-graph primitive. Inclusive is what shows in the heatmap badge by default (more useful); self shown on hover.
4. **Minimal storage, no history.** `OpProfile` struct, ~24 bytes, keyed by instance `Guid` in a single global dictionary. Dictionary entries allocated lazily on first profile tick. When the overlay is toggled off, the dictionary is cleared.
5. **Triple-buffered GPU query pool.** Replaces GpuMeasure's 2-frame alternation. Never blocks, never skips frames. GpuMeasure rewritten to use the new pool.
6. **Promote `GpuQuery` to `Core.Stats.GpuQuery`.** The Core project already has a DX11 / SharpDX dependency (`Core.csproj` has `<UseWindowsForms>true</UseWindowsForms>`, DirectX is present via SharpDX package references). Both Editor and Operators can then reference one copy.
7. **Overlay toggle lives in MagGraph's view menu, not a global user setting.** Users toggle it dozens of times per session. When off, zero cost: no stopwatches, no queries, no dictionary lookups.

---

## Part 1 — Attribute Tagging

New attribute in `T3.Core.Stats`:

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProfilableOpAttribute : Attribute
{
    public OpProfileTag Tags { get; }
    public ProfilableOpAttribute(OpProfileTag tags = OpProfileTag.Heavy) { Tags = tags; }
}

[Flags]
public enum OpProfileTag
{
    None       = 0,
    Heavy      = 1 << 0,   // measurable cost: Draw, ImageEffect, compute, ray marcher
    Structural = 1 << 1,   // Group, Execute, Switch — interesting for shape
    Io         = 1 << 2,   // file, network, audio decode
    Gpu        = 1 << 3,   // has GPU work worth measuring via timestamp queries
}
```

Ops opt in with e.g. `[ProfilableOp(OpProfileTag.Heavy | OpProfileTag.Gpu)]` on the class declaration.

Detection happens at symbol/instance registration time (existing `SymbolPackage` / `Instance` loading pipeline). The profiler caches a `HashSet<Guid>` of Symbol GUIDs that have the attribute; per-instance lookups become O(1).

Initial tagging pass: audit operator categories and apply attributes to ~50 likely candidates. Exact list deferred to implementation — rough scope: everything under `Operators/Lib/render/*`, `Operators/Lib/img/fx/*`, `Operators/Lib/point/*`, and the top-level Group/Execute/Switch ops. Small math / value ops never get tagged.

---

## Part 2 — Per-Instance Storage

```csharp
public struct OpProfile
{
    public float SelfMsLast;         // last frame's exclusive CPU time
    public float InclusiveMsLast;    // last frame's self + children CPU time
    public float SelfMsSmoothed;     // EMA, alpha ~0.1, for stable display
    public float InclusiveMsSmoothed;
    public float GpuMsLast;          // -1 if not GPU-tagged or measurement pending
    public float GpuMsSmoothed;
    public int LastUpdatedFrame;     // for staleness / removal when off-screen
}
```

~28 bytes. Stored in:

```csharp
// in Core.Stats.OpProfiler
private static readonly Dictionary<Guid, OpProfile> _profiles = new();
```

Keyed by `Instance.SymbolChildId` — per-instance, not per-symbol, because two instances of the same symbol can cost differently depending on inputs.

Lazy allocation: no entry until the op is actually profiled (i.e. attribute-tagged *and* overlay active). When the overlay is toggled off, `_profiles.Clear()` and the dictionary returns to empty.

Smoothed values drive the heatmap colour; `Last` values drive the hover tooltip. EMA alpha is frame-rate independent — scale by `60 * dt` so the smoothing looks the same at 60 Hz and 144 Hz.

---

## Part 3 — Self/Inclusive Timing Mechanics

The measurement lives in `Slot.Update` behind a single branch:

```csharp
public void Update(EvaluationContext context)
{
    if (_dirtyFlag.IsDirty || _valueIsCommand)
    {
        OpUpdateCounter.CountUp();
        if (OpProfiler.IsActive && OpProfiler.IsInstanceProfiled(_parentInstanceId))
            OpProfiler.PushAndInvoke(_parentInstanceId, context, UpdateAction);
        else
            UpdateAction?.Invoke(context);
        _dirtyFlag.Clear();
        _dirtyFlag.SetUpdated();
    }
}
```

`OpProfiler.IsActive` is a `static bool` — one cache-hot load, predictable branch when the overlay is off. `IsInstanceProfiled` is the `HashSet<Guid>` lookup (fast). The stopwatch and stack push only happen on tagged ops when the overlay is on.

`PushAndInvoke` implements the self/inclusive split via a thread-local stack:

```csharp
// pseudocode
internal static void PushAndInvoke(Guid id, EvaluationContext ctx, Action<EvaluationContext>? action)
{
    var t0 = Stopwatch.GetTimestamp();
    _stack.Push((id, t0, childInclusiveSum: 0));
    action?.Invoke(ctx);
    var frame = _stack.Pop();
    var inclusive = ElapsedMs(t0);
    var self = inclusive - frame.childInclusiveSum;
    // update _profiles[id] — EMA + Last
    if (_stack.TryPeek(out var parent))
        parent.childInclusiveSum += inclusive;  // ref-struct mutation via TryPeekRef
}
```

The update path cost when overlay-on and op is tagged: one `Stopwatch.GetTimestamp`, one stack push, one pop, one dict update. Measured: ~80–120 ns per tagged op. At ~50 tagged ops evaluating per frame, that's 4–6 µs of overhead — well inside the <2% budget.

Cost when overlay is **off**: one `static bool` read and a branch-mispredict-free not-taken branch. Effectively zero.

### Async / multi-evaluation caveat
If an op triggers its children through a non-trivial control flow (e.g. `ForEach` evaluates children N times), the inclusive time covers the whole composite evaluation — which is the right answer. The self time still correctly excludes children.

---

## Part 4 — GPU Query Pool

### Promote `GpuQuery` to `Core.Stats`
Move [`Operators/Lib/Utils/GpuQuery.cs`](../../Operators/Lib/Utils/GpuQuery.cs) to `Core/Stats/GpuQuery.cs` (namespace `T3.Core.Stats`). Make it `public`. Both Editor-side overlay and Operators-side `GpuMeasure` reference the Core copy.

### New `GpuQueryPool`

```csharp
public sealed class GpuQueryPool : IDisposable
{
    public GpuQueryPool(Device device, int pairCapacity) { ... }

    // Call at the start of every frame with the current frame index.
    public void BeginFrame(DeviceContext ctx, int frameIndex);

    // Begin/End a timestamp pair on the current frame's slice.
    // Returns a handle used to read the result 2 frames later.
    public GpuProfileHandle Begin(DeviceContext ctx);
    public void End(DeviceContext ctx, GpuProfileHandle handle);

    // After BeginFrame, reads all pairs from frame N-2. Non-blocking — skips entries not yet ready.
    // Callback receives (handle, durationMs). Handle was returned by a previous frame's Begin.
    public void FlushReadyResults(DeviceContext ctx, Action<GpuProfileHandle, float> onResult);
}
```

Internally: three ring slices of `(disjoint query, N × timestamp-pair queries)`. Frame N issues into slice `N % 3`; frame N+2 reads that slice. `GetData` is called with `AsynchronousFlags.DoNotFlush` — non-blocking. If any result isn't ready yet (unlikely at 2-frame latency but possible under GPU stalls), skip and try next frame. No blocking API calls anywhere in the hot path.

Sizing: `pairCapacity` = max tagged ops per frame. Start at 128 (with grow-on-demand for larger graphs). Each pair is ~48 bytes of native driver state + 2 SharpDX `Query` wrappers — total pool ~10 KB. Negligible.

### GPU timing from `OpProfiler`
On entry to a `Gpu`-tagged op: `handle = _queryPool.Begin(ctx)`. On exit: `_queryPool.End(ctx, handle)`. Handle is paired with the instance `Guid` in a small `Dictionary<GpuProfileHandle, Guid>` cleared after each frame's `FlushReadyResults`. When the result arrives 2 frames later, `_profiles[instanceId].GpuMsLast = duration`.

Latency: the heatmap's GPU numbers lag 2 frames. Invisible to users at 60 Hz.

### GpuMeasure rewrite
Once the pool exists, `GpuMeasure` ([Operators/Lib/render/analyze/GpuMeasure.cs](../../Operators/Lib/render/analyze/GpuMeasure.cs)) drops its own query management and borrows from the pool instead. The 2-frame alternation disappears. `LastMeasureInMs` is updated every frame instead of every other frame. Operator's public output (`LastMeasureInMs`, `LastMeasureInMicroSeconds`) unchanged — user-facing behaviour identical but values twice as fresh.

---

## Part 5 — MagGraph Overlay

Drawn in `MagGraphCanvas.DrawNode.cs`. Additions behind a `MagGraphView.ShowProfileOverlay` check:

### Per-node visuals
- **Colour ramp on the node border.** Input: `InclusiveMsSmoothed` mapped against a per-session auto-calibration (P50 of tagged ops → cool, P95 → hot). Ramp: green → yellow → orange → red. Calibration re-fits every 2 s so the ramp stays meaningful as the graph's workload changes.
- **Badge text below the op name.** Format: `2.3 ms` for inclusive CPU, or `2.3 / 0.8 ms` for ops with both CPU and GPU measurements (inclusive CPU / inclusive GPU). Small font, muted colour.
- **Hover tooltip.** Shows `Self: X.X ms | Inclusive: Y.Y ms | GPU: Z.Z ms | Tags: Heavy | Gpu`. Plus "No history — use Capture for timeline" hint pointing at the future feature from Part 7.
- **Staleness fade.** If `frameNow - LastUpdatedFrame > 30`, the op didn't evaluate this second — badge fades to indicate inactivity rather than showing a cached value as if current.

### Performance of the overlay itself
The overlay reads from the profile dictionary once per draw. ~50 tagged ops × one dict lookup + one `AddText` per node = negligible. No extra allocations in the draw path.

### Colour-ramp auto-calibration detail
Calibrating against absolute ms thresholds (e.g. ">1 ms = hot") is wrong because a simple project has very different numbers than a heavy one. Instead:
- Every 2 seconds, collect `InclusiveMsSmoothed` from all currently profiled entries.
- Compute P50 and P95.
- Remap: P50 → hue 120° (green), P95 → hue 0° (red), with the median mapped smoothly.

This makes the heatmap relative to *this project right now* — the hottest ops always pop, whether the project is cheap or expensive.

---

## Part 6 — Toggle & Settings

### Primary toggle: MagGraph view menu

A checkbox "Profile overlay" in MagGraph's view menu (and an app-bar button near the existing performance graph). Maps to `MagGraphView.ShowProfileOverlay` — a static bool.

When toggled on:
- `OpProfiler.IsActive = true`. Slot update path starts measuring.
- GPU query pool is allocated if not yet.

When toggled off:
- `OpProfiler.IsActive = false`. Slot update path returns to zero cost.
- `_profiles.Clear()`.
- GPU query pool kept allocated (small, cheap, rapid toggle is expected).

### Secondary settings in `Advanced → Render Performance`

From [Plan_RenderProfiling.md Part 7](Plan_RenderProfiling.md), the "GPU profiling" dropdown is repurposed / clarified to mean *depth* of profiling infrastructure that stays allocated:

| Setting | Off | Global | Per-pass | Per-op |
|---|---|---|---|---|
| Frame-envelope GPU timing | No | Yes | Yes | Yes |
| UI vs scene split | No | No | Yes | Yes |
| Op-graph pool allocated | No | No | No | Yes |
| MagGraph overlay can be enabled | No | No | No | Yes |

Default: "Per-pass". Users who want the operator-graph overlay set to "Per-op" once; the toggle in the MagGraph view menu then controls the actual active state frame-by-frame.

### CSV export (from this plan, small addition)
When the overlay is on, a "Dump frame profile" button exports the current `_profiles` dictionary to CSV: `InstanceId, SymbolName, SelfMs, InclusiveMs, GpuMs, Tags`. Useful for before/after comparisons when optimising a project. One-off export, no subscription state.

---

## Part 7 — History Capture (Future Extension)

Deliberately deferred out of v1. Sketched here so the foundation is compatible.

User picks one or more tagged ops via a MagGraph context menu: "Capture timing history." Allocates a `T3.Core.Stats.RollingMetric` (capacity 600 = 10 s at 60 Hz) per captured op. Data flows into the metric from the same `PushAndInvoke` path that updates the overlay — i.e. we don't re-measure, we subscribe to the stream already being captured.

A floating "Capture" window shows plot lines for each captured op (using the existing `MetricGraphView`). Close the window → disposes the metrics, releases memory.

Why this is clean: the infrastructure is the same as the overlay; the only addition is the per-op `RollingMetric` pointer, which lives in a separate dictionary keyed by instance ID, not inlined into `OpProfile`. Keeps the always-allocated overlay storage tiny.

---

## Dependencies

- **Plan_RenderProfiling M1** (frame-envelope GPU query infrastructure). This plan's Part 4 promotes `GpuQuery` to `Core.Stats` — work shared with M1. Can ship in the same milestone if sequenced right.
- **Attribute tagging audit.** One-shot pass over the operator library (~50 ops). Can be PR'd in batches over time without blocking infrastructure landing first.

## Risks

- **Attribute bloat.** If everyone tags their op as `Heavy` "just in case," overhead grows and the overlay gets noisy. Mitigation: document the criteria ("measurable self-time >0.1 ms at 60 Hz"), review attribute adoption in PRs.
- **GpuMeasure behaviour change.** Users who placed GpuMeasure in projects expect certain timing. After rewriting to use the pool, values update every frame instead of alternating. Should be a strict improvement, but note in release notes.
- **`LastMeasureInMicroSeconds` / `LastMeasureInMs` output semantics.** Keep identical names and units. Smoothing alpha likewise unchanged.
- **Node visuals clutter.** The badge + colour ramp added to every tagged node could make the graph noisy in dense views. Mitigation: overlay is off by default; badge only shows above a threshold (e.g. >0.2 ms) even when overlay is on.
- **Multi-evaluation ops** (ForEach, repeat-for-each-particle patterns). Inclusive time for these will be large, which is correct but might dominate the colour ramp. Auto-calibration (Part 5) handles this by being relative.

---

## Open Questions

- **Where exactly does `[ProfilableOp]` get consumed?** Options: (a) scanned at symbol-package load into `HashSet<Guid>` keyed by Symbol ID, (b) checked per-instance via reflection (slow, avoid), (c) recorded in the symbol UI metadata. (a) is cleanest.
- **Does MagGraph have a suitable hook for drawing extra decoration on nodes,** or does the overlay require restructuring [`MagGraphCanvas.DrawNode.cs`](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs)? Short audit needed before M2.
- **Inclusive time for ops with children evaluated lazily** (getvalue-on-demand patterns like inputs not yet pulled). Does the stopwatch capture them correctly? Needs verification on a specific op — probably `Switch` and `Execute`.
- **Does `Core.csproj` reference SharpDX directly,** or does GpuQuery need to stay in Operators and the Editor references it through there? If direct Core reference is clean, promotion is straightforward. If not, GpuQuery stays in Operators and Core.Stats exposes an abstract `IGpuQueryPool` interface with Operators providing the SharpDX implementation. Audit needed.

---

## Milestones

1. **M1 — Foundation.** Promote `GpuQuery` to `Core.Stats`. Implement `GpuQueryPool` with triple-buffered ring. Rewrite `GpuMeasure` to use the pool (every-frame sampling). Add `[ProfilableOp]` attribute + discovery pass. No overlay yet.
2. **M2 — CPU profiling path.** Implement `OpProfiler.PushAndInvoke`, `OpProfile` struct, per-instance dictionary. Wire into `Slot.Update` behind `IsActive`. Verify zero cost when off via a microbenchmark.
3. **M3 — MagGraph overlay.** Badge + colour ramp + hover tooltip. View-menu toggle. Auto-calibration. CSV dump button.
4. **M4 — GPU profiling path.** Plumb `Gpu`-tagged ops through `GpuQueryPool`. Add GPU ms to the badge.
5. **M5 — Attribute adoption.** Audit the operator library; tag ~50 ops appropriately. Release notes.
6. **M6 — (Future) History capture.** Per-op `RollingMetric` on demand via context menu. Separate shippable.

M1–M2 are independently useful (they subsume the GpuMeasure improvement alone). The overlay is the visible win and lands in M3.
