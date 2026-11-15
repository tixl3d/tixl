# Plan: Context Variable Invalidation

Status: **parked 2026-11-13.** Phase 0 shipped. The rest is not worth its complexity, judging by the Phase 0
baseline: variable getters cost at most a few updates per frame, and the one big cost, ops reading the requested
resolution, was solved by a targeted patch instead (below). Kept for the analysis, the baseline script and the
design, in case variables show up in a profile later.

## What Shipped Instead: Resolution Patch

The same pattern `[MidiInput]` uses: outputs that read the requested resolution are cached (`DirtyFlagTrigger.None`)
and switched to `Animated` only when the resolution changes.

- `EvaluationContext.RequestedResolutionChanged` (Core), raised by two calls:
  - `NotifyPullResolution`: for code that pulls a graph from outside. `OutputUi.StartInvalidation` covers every
    editor output view and preview; the state is kept per `EvaluationContext`, which is one per view.
    `OutputContentResolver.PullContent` covers the Board and the Player; the state is kept per send, since sends
    share one context. A view reports a change only when its own size changes, so views at different sizes
    don't disturb each other. A view resuming after a frame without a pull (a hover preview, a reappearing
    window) reports a change if any view pulled at another size in this or the previous frame, or if none
    pulled at all, because shared ops may be cached at another size. Without this, a reappearing view kept
    showing another view's size. An earlier version reported every resumed pull, which made each new hover
    re-render all resolution-dependent graphs.
  - `NotifyResolutionWrite`: for ops setting a size for their subgraph (`RenderTarget`, `SetRequestedResolution`,
    `SetRequestedResolutionCmd`, `SliceViewPort`, `SendToOutput`, `RenderToCubeMap`, `VisualTest`). It compares
    against that op's own last write, so restoring the outer size isn't a change. On a change the op re-walks its
    subgraph, because this frame's invalidation pass already ran.
- `Operators/Lib/Utils/RequestedResolutionTracker.cs` switches the outputs back to `None` after a whole frame
  without a change. An instance pulled at two sizes in one frame sees a change on every pull and stays animated.
  It is used by `[RequestedResolution]` and by `[GetTextureSize]` in its "use context" mode. That op is the
  second resolution accessor: image ops use it internally, and it was `Animated` in that mode.
- Result: A1 (three image ops at the inherited resolution) went from **77 to 0** updates per frame.
  [`resize_check.py`](context-variable-baseline/resize_check.py) checks that a size change still reaches the
  chain: 256 → 640×360 → 256 through `[SetRequestedResolution]`.
- Not covered: ops that read `context.RequestedResolution` directly and are plain cached value ops
  (`GridPosition`, `CPointToCamera`, `PointToMatrix`, `ProjectOntoStage`, `LoadSvgAsTexture2D`). They were already
  stale on a resize before the patch and are unchanged. Each could adopt the tracker if it matters.
- Tested with the debug bridge's `probePull` as a second view
  ([`multi_view_check.py`](context-variable-baseline/multi_view_check.py),
  [`continuous_view_check.py`](context-variable-baseline/continuous_view_check.py)):
  - a second view pulling another graph at 256×256 on every frame leaves the output's chain cached (0 updates);
  - a view that pulls again after a pause gets its own size;
  - a second view pulling the same chain on every frame gets 256×256 on every frame. The output window in Fill
    mode then shows that texture, by its existing reuse rule, and returns to its own size once the probe stops.
  - A hover-like view pulling the output's chain now and then at the output's size
    ([`hover_check.py`](context-variable-baseline/hover_check.py)) no longer re-renders it. It went from 3 chain
    updates per pull to 1, and that 1 is the probe's own forced pull.
  - An intermittent view at another size (thumbnails) still re-evaluates on each pull: correct, and no worse
    than before.
  - [`session.sh`](context-variable-baseline/session.sh) runs a check in a fresh probe editor and reports any
    save it did. It must not run while another editor is open.
- Limit, not caused by this patch: a chain shared by two scopes at different sizes in the same frame
  ([`shared_scopes_check.py`](context-variable-baseline/shared_scopes_check.py): one chain under two
  `[SetRequestedResolution]` at 256 and 640 feeding one `[Blend]`) gives both the first size. After the first
  scope the chain's slots are clean and nothing re-walks them. A slot holds one value anyway, so even a re-render
  would leave both scopes holding the same resized texture. By reasoning (not re-tested on the old code),
  `Always` behaved the same, only re-rendering once per frame.

## Goal

Context variables (`[SetFloatVar]` → `[GetFloatVar]`, `RequestedResolution`, the `[Loop]` index) should make
their readers dirty **only when the value they read actually changed**. Today every reader is dirty on every
frame, and so is everything downstream of it.

Side effects we want:

- Graphs that depend on `[RequestedResolution]` (image chains sized to the output) can stay cached.
- One central table of variables, which an editor window can show, snapshot and break on.

## How It Works Today

- Variables live in five `Dictionary<string, T>` on `EvaluationContext` (`Core/Operator/EvaluationContext.cs:162`).
  Vec2, Vec3 and Matrix are boxed into `ObjectVariables`, so `[SetVec3Var]` allocates on every write.
- Every `Get*Var` output is `DirtyFlagTrigger.Animated`, and `[RequestedResolution]` is `Always`. Their whole
  downstream re-evaluates every frame. In the Lib/Examples `.t3` files, `[GetFloatVar]` appears in 58 symbols
  and `[RequestedResolution]` in 33.
- Every `Get*Var` calls `context.*Variables.Keys.ToList()` on each update, only to fill its name dropdown. That is
  an allocation per op per frame (`GetFloatVar.cs:17` and its seven siblings).
- Setters have two modes. **Scoped** (`SubGraph` connected): write, evaluate the subgraph, restore the
  previous value. **Frame** (`SubGraph` unconnected): the value stays set for everything evaluated later in the
  frame and is cleared by `EvaluationContext.Reset()` at the start of the next one. Frame mode is the one
  that builds clean graphs, e.g. several setters grouped in a `[Group]` or `[Execute]` ahead of the consumers,
  so it is the main case to optimise, not a fallback.
- `RequestedResolution` is a plain property. It is written in about 10 places in Core and Editor (output window,
  compositor, thumbnails, variation canvases) and in operators (`RenderTarget`, `SetRequestedResolution`,
  `SetRequestedResolutionCmd`, `SliceViewPort`, `SendToOutput`, `RenderToCubeMap`, `VisualTest`). It is read
  directly in 22 operator files.
- `[Loop]` writes the index, then does `GlobalInvalidationTick++` and `Command.InvalidateGraph()` on every
  iteration.
- Command slots always run their update (`Slot.cs:202`: `IsDirty || _valueIsCommand`), so caching only pays
  off on **value** chains: numbers, buffers, textures. That is where the gain is.

## The Hard Part: Timing

The invalidation walk runs **before** evaluation, but most variables are written **during** evaluation (a setter
writes, then pulls its subgraph). So the frame-start walk cannot know a variable changed.

The notes sketch a flow where the change is seen and the reader is dirty on the *next* frame. That would show
a stale value for one frame after every change, for example one frame at the old resolution after a resize.
This plan avoids that lag. A getter remembers which writer it last read from, and in the frame-start walk it
treats that writer's value input as if it were connected to its own input. A writer whose value changes then
makes its readers dirty in the same walk.

Two other traps rule out the simplest design, a write counter per variable:

1. **Scoped restore counts as a change.** A setter writes 512×512 and later restores 1920×1080, so the counter
   moves twice per frame and every reader looks dirty forever.
2. **One instance, several values per frame.** The same subgraph is pulled by the output window, Board
   previews and thumbnails at different resolutions, and inside `[Loop]` with different indices. Each of these
   uses its own `EvaluationContext`. A reader must notice that the value differs from what *it* last produced,
   not merely that a write happened.

## Design

### Variable slots and value epochs

- **`VariableRegistry`** (Core, static) hands out a storage index per `(name, type)` from a counter.
  Registration returns the index, and it is **immutable for the session**: never reused, renumbered or removed,
  even when the last op using the name is deleted. A cached index can therefore never point at the wrong
  variable, and nothing has to be notified or validated.
  - Setters and getters register when their name input changes and keep the index in a field. Per-frame
    access is a plain array read or write: no string hashing and no dictionary lookup. Today every `Get*Var`
    and `Set*Var` hashes its name on every evaluation, and `[Loop]` hashes twice per iteration.
  - Types are part of the key, so `[GetFloatVar]` and `[GetIntVar]` with the same name get separate indices.
    That is the same as today's separate dictionaries.
  - Context arrays grow only when an index beyond their length is first used. That happens at registration
    time or in the first frame after it, never in steady state.
  - Unused names leak a few bytes until restart. That's fine for a session, and the inspector can hide entries
    that have no reader and no writer.
- **`EvaluationContext`** holds typed slot arrays indexed by that int. Each slot is
  `(T Value, int Epoch, bool IsSet)`. One array per type (`float`, `int`, `bool`, `Vector2`, `Vector3`,
  `Matrix4x4`, `string`, `object`), so values are not boxed.
- An **epoch** names a value, not a write. Epochs come from one global counter, so they are unique across
  contexts. A writer keeps the last value and epoch it wrote. If it writes the same value again it reuses that
  epoch; only a different value gets a new one.
- A **scoped write** saves the previous `(Value, Epoch, IsSet)` and restores all three together. Restoring is
  then not a change. This fixes trap 1.
- `RequestedResolution` becomes a built-in slot of type `Int2`. The property stays as a wrapper, so call sites
  keep compiling, but its setter cannot reuse epochs, because a plain property setter does not know which
  writer called it. Writers in hot paths move to the scoped API in Phase 2.

### Readers

- A reader stores the epoch it last read. It is **stale** when the slot's current epoch differs. This fixes
  trap 2: an instance pulled at 512 and then at 1920 within one frame sees two epochs and updates both times,
  exactly as often as today and no more.
- `Get*Var` outputs drop `DirtyFlagTrigger.Animated` and become plain cached slots.
- Readers in C# (`RenderTarget` inheriting the resolution, `SliceViewPort`, ...) use a small reader struct field,
  for example `_resolutionReader.Read(context)`, which records the epoch and registers the op. An op that is not
  migrated keeps reading the property and behaves exactly as today.
- Writer links and the per-slot last-reader list hold instance references for the session only. They are
  cleared on dispose and hot reload, because instances are recreated then.

### Readers link to the writer they read from

A slot is `(Value, Epoch, Writer)`. Every write stores its writer: the setter instance, or a host such as the
output window. A scoped write saves and restores all three together. When a getter evaluates, it records the
writer of the value it actually read. **The evaluation discovers the dependency**, so nesting, shadowing and
branches come out right without any extra graph analysis: a getter inside a nested scope links to the inner
setter, and a getter after a `[Execute]` group links to the frame setter that ran last.

In the next invalidation walk, the getter's output uses an `InvalidationOverride`, the hook `MultiInputSlot`
and `TimeClipSlot` already use to replace the default walk. It walks its recorded writer's **value input** as
if it were connected:

- **Op writer:** the getter is dirty if the setter's value input is dirty. This is the same walk and the same
  frame, with no extra pass, and it works for all three lifetimes. In a scoped setter, the getter sits inside
  `SubGraph` and the value input is a separate branch, so there is no cycle. The tick dedupe means the value
  input is walked once, whoever reaches it first.
- **Host writer** (resolution from the output window, thumbnails): the host has no inputs to walk, so the getter
  compares epochs instead. Hosts write before the frame's walk, so the comparison sees this frame's value.

Recording only happens when the getter evaluates, that is when it was dirty, which matches your "if a getter
is dirty it registers for its setter". There is no global list per variable name.

**Two cases evaluation alone can't see:**

1. **One getter, several values per frame.** A shared subgraph is pulled at two resolutions, or a getter sits
   inside `[Loop]`. The getter records only its last writer. Rule: a getter that read two different epochs
   within one frame stays dirty for the next frame. Its output really does change during the frame, so it
   can't be cached anyway. `[Loop]` keeps its forced invalidation for the command replay regardless.
2. **The writer changes while no input does.** A `[Switch]` starts running a different `[Execute]` group that
   sets the same variable. The getter is still linked to the old setter, whose inputs didn't change. To catch
   this, a write whose epoch differs from the previous frame's write of that slot force-invalidates the
   getters that last read that slot, so they update **one frame late**. This needs a small per-slot list of
   last readers, used only on this path. The inspector counts these late updates.

A deleted or bypassed writer bumps the graph-structure version, which forces its linked getters dirty once.

**Top-level writers** (output window, `OutputContentResolver`, thumbnail and variation renderers) treat the
whole graph as their scope. They already write the resolution before the frame's invalidation walk
(`OutputContentResolver.PrepareContext` sets it, then bumps the tick), so marked readers are picked up by that
walk. No extra walk is needed, as long as the write still comes before the walk; Phase 2 makes that ordering
an explicit requirement.

### Variable lifetimes

Each write has one of three lifetimes:

| Lifetime | Set by | Cleared |
|---|---|---|
| **Scoped** | Setter with `SubGraph` connected | Restored when the setter returns |
| **Frame** | Setter without `SubGraph` | At the start of the next frame |
| **Persistent** | Setter with a `Persistent` option (new) | Only explicitly (see open questions) |

The start-of-frame clear only resets `IsSet`. A writer that writes the same value again in the next frame
reuses its epoch, so clearing and re-setting the variable is not a change.

### Readers that run before their writer

A getter evaluated before its writer in the same frame sees the previous state: unset for a frame variable
(as today), and **the previous frame's value for a persistent variable**. That is the intended feedback
behaviour. A persistent accumulator (`x = x + 1`) links the getter to a setter whose value input depends on
that getter. The tick dedupe stops the cycle, and the getter is dirty every frame, which is correct for a value
that changes every frame.

### Persistent variables

- They survive `Reset()`. They are stored per `EvaluationContext`, so thumbnail and variation contexts don't
  see the output's state. That needs to be clear in the UI.
- They make the inspector's snapshot/restore genuinely useful. Restoring a snapshot means restoring state, not
  only looking at it.
- They cover patterns that today need a hand-written op with a private field: counters, latches, "value at
  last beat".

**Legacy dictionary writers.** User packages may write `context.FloatVariables[...]` directly. The dictionaries
stay as `[Obsolete]` shims. A reader that finds its slot unset falls back to the dictionary and stays `Animated`
while it does.

### `[Loop]` and iteration ops

Keep the per-iteration force invalidation. The command subgraph has to replay its draws on every iteration
regardless. Move them to the slot API so they show up in the debug table and stop writing dictionaries.

## Phases

### Phase 0: Quick wins and a baseline (no Core change)

- **Done 2026-11-12.** The per-frame `Keys.ToList()` (and LINQ in `[GetMatrixVar]`) is gone from all eight
  `Get*Var` ops. `Operators/Lib/Utils/VariableNameOptions.cs`: the dropdown calls `Request()` and the op's next
  update refills one reusable list. Names are collected during evaluation, so names from scoped setters still
  show up. Reading the context lazily after the frame would miss them. Unselected ops never enumerate. The
  Vec2 and Vec3 dropdowns now list only names that hold that type, as Matrix already did. The list appears one
  frame after the dropdown is first drawn. This relies on the ops being `Animated`; once Phase 3 makes them
  cached, `Request()` must also invalidate the output.
- **Baseline measured 2026-11-12** (Debug editor, Linux/Vulkan, playback paused, nothing changing). Script:
  [`context-variable-baseline/baseline.py`](context-variable-baseline/baseline.py). It builds the graphs in
  `_agentTests` over the debug bridge. Back up that project first, because autosave can keep the probe ops.
  The metric is the new bridge method `getUpdateStats`: output-slot updates in the previous frame below the
  focused composition. CPU time (`getFrameSamples`) is the whole editor frame and only moves in the C1 case.

  | Graph | Updates/frame |
  |---|---:|
  | A1 `[CheckerBoard]` → `[Blur]` → `[Blur]`, inherited resolution | 77 |
  | A2 same, explicit 512×512 on all three | **0** |
  | B `[Loop]` ×20 → `[Transform]` (`[GetFloatVar]` i × 0.3) → `[DrawMesh]` sphere | 581 |
  | C0 `[Execute]` with 3 frame-mode `[SetFloatVar]` + a sphere draw, no getters | 30 |
  | C the same, `[Vector3]` translation from three `[GetFloatVar]` | 34 |
  | C1 C0 plus `[GetFloatVar]` x → `[SphereMesh]` Radius | 38 (+0.2 ms CPU, mesh regenerated every frame) |

  What the numbers say:
  - **Resolution is the big win (Phase 2).** Image ops at the inherited resolution re-render completely every
    frame (about 25 updates each, including `RenderTarget` and `Draw`), because the image ops contain
    `[RequestedResolution]` internally. With an explicit resolution they cost nothing. This is also GPU work.
  - **Getters only pay off when they feed a value chain** (C1: a mesh is regenerated). A getter feeding a
    Command op such as `[Transform]` saves almost nothing (C: 4 updates), because Command slots replay anyway.
  - **`[Loop]` won't improve through variables.** 581 ≈ 20 × 29. That is the cost of one `[DrawMesh]` per
    iteration, and it doesn't depend on the index getter. It comes from context reads that are always dirty:
    `[GetPbrParameters]` (`Always`, reads `context.PbrMaterial`), `[ContextCBuffers]` (`Animated`),
    `[Vector4Components]`. They make every draw cost about 30 updates even in C0. That makes open question 2
    (other context fields as slots) worth more than this plan assumed.
- Found on the way and fixed: `DirtyFlag.NumUpdatesWithinFrame` returned 0 for a slot that updated more than
  once in the previous frame. Its only other user is the legacy graph's update indicator.

### Phase 1: Central table in Core

- `VariableRegistry`, typed slot arrays on `EvaluationContext`, `Reset()` clears the slots, not the indices.
- API on `EvaluationContext`, allocation-free:
  - `SetVariable(index, value, ref WriterState)` for unscoped writes,
  - `PushVariable(...)` / `PopVariable(...)` returning a saved-state struct for scoped writes,
  - `TryGetVariable(index, ref ReaderState, out value)`.
- Dictionaries become obsolete shims (see above).
- Unit tests in the existing xUnit project: epoch reuse, scoped restore keeps the epoch, nested scopes shadow,
  a reader alternating between two contexts goes stale both ways.

This is Core API with a long lifetime, which `AGENT_INSTRUCTIONS` asks to keep minimal. It has to live in
Core, because the Player evaluates these ops too.

### Phase 2: RequestedResolution

- Make the property a wrapper around the built-in slot.
- Top-level writers write before the invalidation walk; check each of the Core/Editor call sites listed above.
- `[RequestedResolution]`: outputs become cached readers instead of `Always`.
- Scoped resolution writers (`RenderTarget`, `SetRequestedResolution`, `SetRequestedResolutionCmd`,
  `SliceViewPort`, `SendToOutput`, `RenderToCubeMap`) move to `PushVariable` / `PopVariable`.
- Audit the 22 operator files that read `context.RequestedResolution` directly. Each becomes a reader or is
  documented as staying dirty. Possible latent bug to check first: `RenderTarget.cs:61` inherits the context
  resolution when its own size is unset, but its texture output is cached, so a resolution change alone may
  not re-render it today. That may currently be masked by the always-dirty `[RequestedResolution]` elsewhere
  in the same graph.

### Phase 3: Set/Get ops

- All `Set*Var` ops use the API: scoped when `SubGraph` is connected, frame lifetime otherwise. Both store
  themselves as the slot's writer.
- All `Get*Var` ops become readers that record their writer and walk its value input; `Animated` only while
  the legacy dictionary fallback applies.
- `Vec2`/`Vec3`/`Matrix` variables get typed slots, which ends the boxing.
- `[Loop]`, `RepeatWithMotionBlur`, `RenderToCubeMap`, `TimeClip`, `_ProcessVideoClips`, the obsolete iterators
  and `ExecuteTests` move to the API.
- Re-run the Phase 0 measurement. Accept only if visits and evaluate time go down on the resolution project,
  and don't go up on the loop project. Add a third project with frame-mode setters grouped in an `[Execute]`,
  since that is the common case.

### Phase 3b: Persistent variables

- `Persistent` option on the `Set*Var` ops, plus whatever clear mechanism open question 1 settles on.
- Slots marked persistent survive `Reset()`.

### Phase 4: Variable inspector (Editor)

- A window listing the variables of the output context: name, type, current value, epoch, writes and new
  epochs per frame, linked getters, one-frame-late updates, and whether the fallback is active. This makes "why is this always
  dirty" answerable.
- Snapshot and restore of the table, so a sub-graph can be inspected under the values it was last evaluated
  with.
- Break on change: pause playback when a chosen variable gets a new epoch, and select the writer.
- Add a `.tests-manual/` set for the window.

## Risks

- **Values that alternate within a frame.** Two outputs pulling one shared graph at different resolutions
  make its getters multi-valued, so they stay dirty: no gain there, but no extra walks either. Phase 3's
  measurement must include a two-output setup to confirm it doesn't regress.
- **Stale readers after graph edits.** A reader that is never re-marked, for example because its writer was
  deleted, would keep an old value. The structure-version bump has to force readers dirty once, not only
  clear writer links.
- **One-frame-late updates** when a different writer takes over without any input change. Rare by
  construction, but visible as a single glitched frame. The inspector counter shows whether it happens in
  real projects.
- **Object variables.** Equality is by reference, so an object mutated in place would not count as a change.
  Keep `[GetObjectVar]` `Animated` unless its writer opts in.
- **Hot reload.** Subscriber lists hold instances; they must be cleared when a package reloads.

## Open Questions

1. When is a persistent variable cleared? Candidates: playback restart or jump to start, project reload, an
   explicit `[ClearVar]` op, or never except by the inspector. They may also need to be saved with the
   project.
2. Should other context fields (`PbrMaterial`, `ObjectToWorld`, camera matrices, `ForegroundColor`) become
   slots too? Same mechanism, but they're written by many more ops. The baseline shows every `[DrawMesh]`
   costs about 30 updates per frame from these reads alone (`[GetPbrParameters]` is `Always`), which is more
   than variables cost in any measured graph except the resolution chain. That's likely its own plan, built on
   Phase 1's slots.
3. Is a reader dropdown fed by the **registry** (all names seen this session) good enough, or must it show only
   the names set in the current context?
