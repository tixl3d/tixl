# Plan: Graph Evaluation Debugger

Status: **idea / not started** (2026-11-12, written against `main` ce3c9182f)

Step through a frame's graph evaluation, set breakpoints and log points on operators, and inspect the
evaluation context (variables, time, transforms) at each step. Similar to stepping through Unreal
Blueprints, but adapted to how TiXL evaluates graphs.

## The constraint that shapes everything

Evaluation is a **synchronous, recursive pull** on the main thread: `Slot<T>.GetValue` → `Update` →
`_effectiveUpdateAction` → the operator's `Update`, which pulls its inputs
(`Core/Operator/Slots/Slot.cs`). The editor UI is drawn on that same thread and in the same frame.
So we can't stop *inside* the recursion: blocking freezes the UI, and the operators' plain C# call
stack can't be resumed later.

**Approach: let the frame finish, but only execute up to step N.** While debugging, each slot's
update action is temporarily wrapped (in the same way Bypass swaps actions). The wrapper counts the
updates. Once the count reaches the step budget N, it stops calling the original action and returns
the cached value, and the frame completes normally. Ops are stateful: they keep their last value,
texture and buffer contents. So the output window shows the result of evaluation **up to step N**.
For a render graph that is a partly drawn image, much like stepping draw calls in RenderDoc. "Step"
re-runs the frame with budget N+1.

A trace of the same pass (one event per update) adds the call tree, stepping backwards, and
"where was this set". Both use the same wrapper.

## What already exists

| Need | Existing piece |
|---|---|
| Per-slot update count within a frame | `DirtyFlag.NumUpdatesWithinFrame`, `WasUpdatedThisFrame`, `FramesSinceLastUpdate` (already used to fade idle nodes in `MagGraphCanvas.DrawNode.cs`) |
| Global slot-update counter | `Core/Stats/OpUpdateCounter.cs` (one static increment in `Slot.Update`) |
| Context variables | `EvaluationContext.{Bool,Int,Float,String,Object}Variables` dictionaries; `Set*Var` ops set, evaluate the `SubGraph`, then restore (`Operators/Lib/Symbols/flow/context/SetFloatVar.cs`) |
| Per-instance log/error | `Log.*(msg, instance)` and `OperatorDiagnostics` |
| Remote driving / screenshots | TCP debug bridge (`Editor/App/DebugProtocol/DebugServer.cs`), see `.agentic/DEBUG_PROTOCOL.md` |

## Design

### 1. Debug wrapper (the only Core change)

`Slot<T>` already builds `_effectiveUpdateAction` from `_updateAction` once, when the action is
assigned (`RebuildEffectiveUpdateAction`, which adds the time-clip remap). Add one more optional layer
there:

```csharp
// Core: internal, set/cleared by the editor's debugger
internal void SetDebugWrapper(ISlotDebugWrapper? wrapper);   // rebuilds _effectiveUpdateAction
```

- **Zero cost when off.** `Update` stays a single delegate call. Unlike a static `IsCapturing` check in
  `Slot.Update`, the hot path does not change at all.
- **Use its own field, not `_keepOriginalUpdateAction`.** That field belongs to Bypass and
  `OverrideWithAnimationAction`. If the debugger shared it, debugging a bypassed or animated op would
  conflict with them. Because the wrapper composes *around* whatever action is current (connected,
  bypassed or animated), it needs no special cases for those.
- **Arming** walks the instance tree of the debugged composition and wraps every output slot. That
  allocates one closure per slot, which is acceptable because the user starts it. When the structure
  changes (add/delete op, hot reload, new instances), re-arm: subscribe to the same change signals the
  graph view uses. **Disarming** removes all wrappers and force-invalidates the composition.
- **Cache hits are not seen** by an update-action wrapper, because `Update` only calls the action
  when the slot is dirty (or is a Command). To answer "why didn't this op update?", the trace also
  needs the clean pulls. Two options: also wrap the slot's `GetValue` path (more Core surface), or
  derive it: a slot that was connected, reachable, and not updated in this pass was a cache hit.
  Decide in Phase 1.
- **Event recording** (when tracing): struct events
  `{ depth, slot, instance path index, valueSnapshot, contextDiffIndex, ticks }` in a preallocated
  buffer. Values are recorded only for small types (float, int, bool, Vector*, Matrix, string ref);
  other types get a summary (list count, texture size/format).
- **Main thread / one root.** Only count and record on the main thread, for the root being debugged
  (default: active output window). Thumbnails, the setup board and the debug server evaluate other
  roots in the same frame with other `EvaluationContext`s, and must pass through without being
  counted.

The recorder, buffers and UI live in `Editor/`. `Core` only gets the wrapper slot and its interface.
The Player never arms it.

### 1b. Live stepping with a step budget

- Playback is paused while stepping (the UI enforces it).
- Every editor frame: invalidate the debugged composition, reset the counter, evaluate. Each update
  increments the counter. While `counter < N` the original action runs. After that the wrapper
  returns without running it, so the value stays from the last run.
- **Skipped slots must stay dirty.** `Slot.Update` calls `_dirtyFlag.Clear()` right after the action
  returns, so a skip would mark the stale value as clean. Collect the skipped slots and
  `ForceInvalidate()` them at the end of the pass, or simply invalidate the whole composition at the
  start of each step frame (needed anyway, see below).
- **Replaying the prefix.** Each step re-runs steps 1..N, because the recursion can't be resumed.
  Pure ops don't care. Time-gated stateful ops (`Damp`, `Spring` skip when time hasn't changed,
  `Damp.cs:24`) don't advance while playback is paused. Ops that advance unconditionally (feedback
  textures, particle sims, `UseAppRunTime`, counters) **do** advance per step. Phase 1 needs a list of
  which Lib ops do that, and the UI should warn when one of them is in the debugged range.
- **Step into / over / out** map to budgets: "over" = advance N to the event after the current
  event's subtree closes (known from the previous pass's trace). "Into" = N+1. "Out" = end of the
  parent's subtree.
- **Run to breakpoint** = budget set to the breakpoint's event index from the trace (or unlimited,
  stopping at the first hit).
- **Output window**: shows the debugged root as usual. Because the skipped ops keep their state, it
  shows "the graph as evaluated up to here". Optionally, render the *current* event's output (texture
  or value) in a small preview next to the call tree.

### 1c. Why not keep the state at the breakpoint and continue from it?

**For inspection we do keep it, and nothing re-runs.** The pass that hits a breakpoint is recorded
(values, context diffs, counters). Looking at the state at the hit, and stepping back and forth
through the recorded events, only reads that recording. Re-running is needed only when the **output
window and GPU resources** should show step N (render targets get overwritten later in the frame),
or after an edit/override.

**For continuing the frame, the context is not enough.** The rest of the frame does not live in the
`EvaluationContext`. It lives in the C# call stack of every op above the breakpoint:

- `[Loop]` is in `for (var i = 0; i < end; i++)` at iteration 3. `i` is a local variable
  (`Loop.cs:23`).
- `[SetFloatVar]` has pulled its `SubGraph` and still has to restore the previous value. `previous`
  is also a local.
- A `RenderTarget` has bound its RTV and will resolve and generate mips after its children return.

Cloning the context gives the state but not the "what comes next". To continue without re-running,
the stack itself has to survive. The only way to do that is to **run the debugged pass on its own
thread and block it at the breakpoint**, while the main thread keeps drawing the UI. That is a real
option, but it is the expensive one:

- **GPU state.** The blocked pass leaves pipeline state half-set (bound RTVs, shaders, viewports,
  cbuffers). ImGui rendering on the main thread would overwrite that state before the pass continues.
  D3D11.1 has `ID3D11DeviceContext1::SwapDeviceContextState` to swap the entire context state, which
  is the right tool on Windows. The code doesn't use it today (only `ClearState`, `ProgramWindows.cs:337`).
  The Vulkan backend needs its own answer (command-buffer recording would have to be suspended across
  UI frames).
- **Thread affinity.** Ops and Core helpers assume the main thread (resource manager, shader
  compilation, `Playback.Current`, logging with instance links, gizmo/ImGui access from some ops). All
  of them would need auditing.
- **Exclusive graph access.** While the pass is suspended, nothing else may evaluate or edit that
  composition: no thumbnails, no other output windows on the same graph, no graph edits. Otherwise the
  suspended stack resumes on changed instances.

Decision for now: inspection uses the recording (no re-run). Showing step N in the output uses the
step budget (re-run). The dedicated-thread approach stays a later option, if re-running turns out to
be a problem in practice (slow graphs, or too many ops that advance on every run).

### 2. Context diffing → "where was this variable set"

Diff the context around each update with a dirty event: the five variable dictionaries plus
`LocalTime`, `LocalFxTime`, `ObjectToWorld`, `WorldToCamera`, `CameraToClipSpace`, `IteratedListIndex`,
`PbrMaterial`. Any difference is attributed to the **innermost update that produced it**.

This works for every op without changing any of them: `SetFloatVar`, `Loop`, `TimeClip`, transform
ops, `AudioReaction`, and so on. It gives:

- **Highlight recently changed:** variables changed by the current step, or within the last N steps.
- **Where was it set:** jump to the last event that wrote the name before the cursor.
- **Restores:** `Set*Var` restores the previous value after its subgraph. The diff shows that restore
  as a separate change, so scoping becomes visible.

The full dictionaries are copied only at capture start (allocations are fine for a user-triggered
capture). After that only the diffs are stored.

### 3. Debugger window (Editor)

- **Call tree:** the trace as an indented tree (op name, output, dirty or cached, value preview, time).
  Filter by op name, symbol and "dirty only".
- **Stepping:** step over/into/out and back. "Into" a compound op follows its children. The graph
  view navigates into that composition (it already navigates by instance path) and highlights the
  current node.
- **Context panel:** the evaluation context at the cursor. Variables are grouped by type, changed ones
  are highlighted, there is a name filter, and each entry has a "where set" link.
- **Counters:** per instance, the number of pulls vs. real updates in the captured frame. Across
  frames, the hit count per breakpoint.
- **Graph overlay while a trace is open:** nodes get their evaluation order number and the update
  count, and the nodes the frame never pulled are dimmed.

### 4. Breakpoints and log points

These are attached to an **output slot of a specific instance**, stored as instance path (Guid list)
+ output id, in editor user settings. They are not stored in `.t3`/`.t3ui`, so they don't end up in
shared projects.

- Implementation: the same debug wrapper (section 1) on just that slot. Slots without a breakpoint pay
  nothing.
- **Breakpoint:** when hit (optional condition: hit count, "value changed", or a simple comparison on
  a scalar value or a named context var), playback pauses and the debugger arms the full composition
  in step mode with N = the hit's event index. The output and context panel then show the state at
  the hit.
- **Log point:** a template like `"{name} = {value}, i={var:index}"` written with
  `Log.Debug(msg, instance)` on update. It never pauses. A rate limit keeps it from flooding the
  console at 60 fps.
- Graph UI: a red/amber dot on the node and a toggle in the node's context menu.

### 5. Overrides (later)

Editing a value in the context panel creates an **override**: before the targeted event's slot
updates, the wrapper writes the edited value into the context. Step mode re-runs the frame every
step, so the edit shows up in the next step frame, live, with the same caveats about ops that advance
on every run.

### 6. Object and texture inspection (later)

- Object vars, `StructuredList`, buffers: type, count, first N elements, in the context panel.
- Textures: only snapshot **opted-in** slots (copy into a pooled texture at that event), because render
  targets are reused later in the frame and a by-reference preview would show the final state, not
  the one at that step. GPU memory cost is shown in the UI.

## Relation to `Plan_ContextVariableInvalidation`

That plan replaces the five variable dictionaries with one central variable table, so readers only get
dirty when the value they read changes. If it lands first, section 2 here becomes simpler and cheaper:
the tracer hooks the table's write path (who wrote, old → new) instead of diffing dictionaries around
every update, and its version numbers show directly why a `Get*Var` reader was or wasn't dirty. The two
plans don't depend on each other: dictionary diffing works today. But the variable-related parts of the
debugger should use that table once it exists rather than build a second mechanism.

## Phases

1. **Probe.** Add the debug wrapper in `Slot`, an arm/disarm walk, and a one-frame trace dump through
   the debug bridge (JSON). Prove the step budget works with debug-bridge commands only (no UI yet):
   step through a small render graph and screenshot the output after each step. Check it on a `Loop` +
   `SetFloatVar` example. Decide how to detect cache hits. List the Lib ops that advance on every run.
   *Done when:* the output shows a partial image at N, the trace shows loop iterations with correct var
   diffs, and disarming restores identical output.
2. **Debugger window.** Call tree, step into/over/out/back, graph navigation and highlighting, context
   panel with change highlighting and "where set".
3. **Breakpoints and log points.** Per-slot wrapping, conditions, auto-pause, per-frame ring capture
   while breakpoints exist, node badges.
4. **Overrides, object and texture snapshots.**

Phase 1 also gives agents two debug-bridge commands, "dump one frame's evaluation" and "evaluate up
to step N", which are useful on their own for graph-bug isolation.

## Open questions

1. ~~Record-and-step instead of a true stop?~~ Decided 2026-11-12: wrap update actions in the same
   way as Bypass, and step by re-running the frame with a step budget. The output shows the ops' last
   state.
2. Breakpoints in user settings (private) or in the project (shareable, e.g. for teaching)?
3. Which evaluation roots to trace by default: only the active output window, or every root in the
   frame?
4. Should commands (`Slot<Command>`, always updated) be shown differently in the tree, since they
   dominate the event count in render graphs?
