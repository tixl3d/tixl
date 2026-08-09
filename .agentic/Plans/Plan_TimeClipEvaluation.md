# Time clip evaluation (make the remap a property of the op, not of one slot)

**Status:** Drafted 2026-08-06. The *model* half of a two-plan pair; the editor/UX half is
[`Plan_TimelineClipUx.md`](Plan_TimelineClipUx.md). This plan changes how time is remapped during evaluation
and how animation curves pick their time space. It has the wider blast radius of the two and should land
first — the UX plan's Global/Local toggle is only coherent once the model below is consistent.

## Goal

A time clip remaps time for **the whole operator**, not for whichever output slot the consumer happened to
pull. Once that holds, "animation inside a clip runs in clip-local time" becomes a rule instead of an
accident, and the local-vs-global choice can be made explicit where a user actually wants it.

## The defect

The remap lives in [`TimeClipSlot<T>.UpdateWithTimeRangeCheck`](../../Core/Operator/Slots/TimeClipSlot.cs#L61)
and is installed by overriding the `UpdateAction` **setter** ([`TimeClipSlot.cs:103-110`](../../Core/Operator/Slots/TimeClipSlot.cs#L103)):

```csharp
public override Action<EvaluationContext> UpdateAction
{
    set
    {
        _baseUpdateAction = value;
        base.UpdateAction = UpdateWithTimeRangeCheck;
    }
}
```

So the remap is bound to **one slot**. An operator that binds the same `Update` to several outputs is
remapped on one path and not on the others. Both multi-output clip ops in the tree do exactly that:

| Op | Outputs | Bound in ctor | Consequence |
| --- | --- | --- | --- |
| [`VideoClip`](../../Operators/Video/lib/io/video/VideoClip.cs#L33) | `Texture` (`Slot<Texture2D>`), `TimeSlot` (`TimeClipSlot<Command>`) | one `Update` on both (`:35-36`) | `Texture` path gets no remap, so the op carries a **manual** remap at `:51-73`. Pulling `TimeSlot` remaps *and then* runs that manual remap. |
| [`MidiClip`](../../Operators/Io/lib/io/midi/MidiClip.cs#L18) | `Values` (`TimeClipSlot<Dict<float>>`), `ChannelNames`, `DeltaTicksPerQuarterNote` | one `Update` on all three (`:21-23`) | Correct through the two plain outputs; **double-remapped** through `Values`, its primary output (`:49-61`). |

Which result survives a frame depends on evaluation order between the two pulls. `_ProcessVideoClips` pulls
`TextureOutput`, so the common video wiring happens to work — by accident of which manual remap runs last.

This is also why [`Plan_VideoClipPlayer.md`](Plan_VideoClipPlayer.md) states that the manual remap at
`PlayVideoClip.cs:33-51` "becomes redundant" once `TimeClipSlot` handles it. It never could: the player does
not pull the clip slot.

### Downstream symptom: which time do curves use?

[`Animator`](../../Core/Operator/Animator.cs#L158) installs a closure on each animated input slot; all eleven
closures sample at `ctx.LocalTime` ([`Animator.cs:188-264`](../../Core/Operator/Animator.cs#L188)) — never
`LocalFxTime`. So an animated parameter inherits whatever time space the enclosing evaluation path produced:

- animated `Color` on a `[VideoClip]` → **global** time (the player pulls `Texture`, which is unremapped)
- an animated parameter inside a `[TimeClip]` subtree → **local** time

Same timeline, same visual metaphor, opposite semantics, with nothing in the UI to indicate which applies.

### Second symptom: keys are written in a different space than they are read

Every key-insert path stamps the **global** playhead:

- [`Animator.cs:101`](../../Core/Operator/Animator.cs#L101), `:132` (`AddCurvesForFloatVector` / `AddCurvesForIntVector`)
- [`Animator.cs:491`](../../Core/Operator/Animator.cs#L491), `:518` (`UpdateVector3InputValue` / `UpdateFloatInputValue`)
- [`ChangeInputValueCommand.cs:20`](../../Editor/UiModel/Commands/Graph/ChangeInputValueCommand.cs#L20)

Inside a non-identity clip, keys therefore land at the wrong time. This is the behaviour the user docs
already warn about in [`.help/docs/using/Timeline.md`](../../.help/docs/using/Timeline.md) — *"Remapped time
interacts with keyframe editing in sometimes-surprising ways."*

## What already exists and works

- [`TimeRangeMapping`](../../Core/Animation/TimeRangeMapping.cs#L24) — allocation-free `readonly struct` with
  `LocalBarsToSourceBars` (`:47`), `SourceBarsToLocalBars` (`:70`), `IsActive` (`:88`) and baked BPM; built via
  `clip.ToMapping(playback)` (`:100`). Its own doc-comment already says new code should publish one of these
  instead of doing the math inline. This is the primitive the whole plan leans on.
- **Compositions remap correctly.** `AddConnection` sets `UpdateAction = ConnectedUpdate`
  ([`Slot.cs:203`](../../Core/Operator/Slots/Slot.cs#L203)), which the `TimeClipSlot` setter override wraps —
  so a sub-composition with a `TimeClipSlot` output does remap its whole subtree. Nested clip content is
  sound today *as long as the consumer pulls the clip slot*.
- **Two ops already do it right by publishing data instead of mutating context.**
  [`LoadDataClip.cs:71-85`](../../Operators/Io/lib/io/data/LoadDataClip.cs#L71) and
  [`LoadMidiFile.cs:67-72`](../../Operators/Io/lib/io/midi/LoadMidiFile.cs#L67) build a `TimeRangeMapping` and
  hand it downstream on the `DataClip`. They never touch `context.LocalTime`, so they cannot double-remap.
- [`AudioClip`](../../Operators/Lib/io/audio/AudioClip.cs#L126) is also safe: `SyncClip` derives
  `SourceOffsetSecs` / `SourceDurationSecs` for the audio engine and never remaps the context. Timing is the
  engine's job ([`SoundtrackClipStream.UpdateSoundtrackTime`](../../Core/Audio/SoundtrackClipStream.cs#L159)).

## Architectural decisions (proposed — confirm before Phase 1)

- **The remap belongs to the child, applied to every output.** Wrap all output slots of a clip-shaped child
  with the same range-check + remap, sharing one `TimeClip`. Single-output clips behave bit-identically; only
  multi-output clips change.
- **One `TimeClip` per child.** `OutputData` is per-*output* today
  ([`Symbol.Child.cs:582-589`](../../Core/Operator/Symbol.Child.cs#L582)), so a child with two
  `TimeClipSlot` outputs would carry two independent `TimeClip`s. That shape is not meaningful on a timeline —
  detect it at instantiation, use the first, and log a warning. Do not try to support it.
- **The remap moves out of `TimeClipSlot<T>` into a shared helper.** `TimeClipSlot` keeps implementing
  `ITimeClipProvider` and owning the `TimeClip`; the wrapping logic becomes callable for plain `Slot<T>`
  outputs of the same child.
- **Ops stop remapping by hand.** After the wrap, `context.LocalTime` inside a clip op's `Update` *is* source
  time. `VideoClip` and `MidiClip` delete their inline math. Ops that publish a `TimeRangeMapping` as data
  (`LoadDataClip`, `LoadMidiFile`) keep doing so — that's a different, correct pattern for downstream consumers
  that need to place events at arbitrary times, not just "now".
- **Curve time space becomes explicit, but only after the default is consistent.** `ctx.Playback.TimeInBars`
  is the untouched global and is already on every context, so a per-curve `Local | Global` choice is a small
  addition. It is deliberately Phase 3, not Phase 1 — an override on top of an inconsistent default would
  entrench the confusion.
- **The marker-interface trio collapses into one enum.** `IPreventingTimeRemap`, `IContentTimeClip` and the
  `TimeClip.UsedForRegionMapping` bool are three encodings of one question: *how does `SourceRange` behave when
  the clip is moved or trimmed?* Replace with a single declaration that also expresses the missing third case
  (normalized).
- **`LocalFxTime` keeps tracking `LocalTime` through the remap.** No change; idle-motion behaviour inside
  clips stays as-is.

## Non-goals

- Non-linear / curve-based time remapping. `SourceRange` → `TimeRange` stays affine so it remains invertible —
  the UX plan's dope-sheet mapping depends on that.
- Reworking `[SetTime]`. It also writes `LocalTime` ([`SetTime.cs:30-47`](../../Operators/Lib/numbers/anim/time/SetTime.cs#L30)),
  and its `GlobalAbsolute` mode deliberately does not restore. It stays as it is; see *Open questions*.
- Any change to `Playback`, BPM handling, or the audio engine's timing.

## Phases

### Phase 1 — Remap every output of a clip-shaped child

**Goal:** the time space an operator sees no longer depends on which of its outputs the consumer pulled.

**Scope:**

- Extract the body of `TimeClipSlot<T>.UpdateWithTimeRangeCheck` into a helper that can wrap any
  `Action<EvaluationContext>` given a `TimeClip`.
- At [`Symbol.Child.cs:582-589`](../../Core/Operator/Symbol.Child.cs#L582), after `SetOutputData` has bound the
  `TimeClip`, wrap the update action of the child's **other** output slots with the same helper and the same
  `TimeClip`. Warn and use the first clip if a child declares more than one `TimeClipSlot` output.
- **The other outputs are remapped but NOT gated.** The `TimeClipSlot` keeps its out-of-range early-return;
  the sibling outputs only get the affine time remap, extrapolated outside `TimeRange`. Reason: the video
  player deliberately pulls `Texture` *before* the clip is active to preroll the decoder
  ([`_ProcessVideoClips`, `PrerollSeconds`](../../Operators/Video/lib/io/video/_ProcessVideoClips.cs)), and a
  gate on that path would silently break preroll. Extrapolated source time plus the op's own clamp reproduces
  today's warm-up behaviour exactly. Guard the degenerate `TimeRange.Duration ≈ 0` case like
  `TimeRangeMapping` does instead of dividing.
- Preserve the existing gate semantics exactly: out-of-range test against the **unremapped** `LocalTime`, and
  the **exclusive end** (`>= End` is outside) so adjacent clips at a cut never both evaluate.
- Fix the `UpdateAction` setter aliasing while here. Today `RestoreUpdateAction()` can set `UpdateAction = null`,
  which the override turns into `_baseUpdateAction = null` plus `base.UpdateAction = UpdateWithTimeRangeCheck` —
  a permanently broken slot that logs *"Ignoring invalid time clip update action"*
  ([`TimeClipSlot.cs:81`](../../Core/Operator/Slots/TimeClipSlot.cs#L81)) every frame. Route the wrapper
  through a field that is not reachable via the public property.
- Sweep the manual remaps that are now double-applied:
  - [`VideoClip.cs:51-73`](../../Operators/Video/lib/io/video/VideoClip.cs#L51) — delete; read
    `context.LocalTime` as source time and convert to seconds. Keep the clamp to the source range and the
    `IsRenderingToFile` / `OpNotReady` gate, but re-express the "is this clip actually active" test, which
    currently compares an unremapped `LocalTime` against `TimeRange` (`:80`).
  - [`MidiClip.cs:49-61`](../../Operators/Io/lib/io/midi/MidiClip.cs#L49) — delete the inline rate math.
  - Leave `LoadDataClip`, `LoadMidiFile`, `AudioClip` alone (they publish or delegate; see above).
- Audit the rest of the tree for ops that read `context.LocalTime` and also own a `TimeClip`. The full
  clip-shaped inventory is `[TimeClip]`, `[AudioClip]`, `[VideoClip]`, `[MidiClip]`, `[LoadMidiFile]`,
  `[LoadDataClip]`, `[ImageSequenceClip]`, plus example symbols under `Operators/Examples/`.

**Behaviour change to communicate:** a parameter animated directly on a `[VideoClip]` moves from global to
clip-local time. Existing projects that animated `Color` / `BlendMode` on a video clip will see those keys
shift. See *Open questions* #1 before starting.

**Testable outcome:**

- A `[VideoClip]` wired into both a `[VideoClipPlayer]` (pulls `Texture`) and a `[Group]` (pulls `TimeSlot`)
  shows the same frame through both paths, at every playhead position, and while scrubbing.
- A `[MidiClip]` consumed through `Values` produces the same events as one consumed through `ChannelNames` +
  a manual lookup, i.e. the double remap is gone. Trimming and slipping the clip behaves identically on both.
- An animated parameter on a `[VideoClip]` moves with the clip when the clip is dragged.
- Existing example projects using video / MIDI clips open and play unchanged, apart from the documented
  animated-parameter shift.

**Effort:** ~1–1.5 days, most of it in the sweep and in verifying no op double-remaps. The Core edit itself is
small.

**Risk:** highest in the plan. It is a `Core` change on the evaluation path. Land it alone, not bundled.

**Status: DONE — verified in-editor 2026-08-09** (guided set `time-clip-evaluation.md` passed 8/8, after
the fixes found during the run: export-wait predicate, slip inversion, disable/re-enable trigger wipe).
As built:

- **Zero cost in `Slot<T>.Update`** (the inner loop): the hot path invokes a precomputed
  `_effectiveUpdateAction` — the same single field-load + null-check + delegate call as before the feature.
  The `UpdateAction` property setter rebuilds the effective delegate on every assignment, baking in the
  sibling-output remap when `_timeClipForOutputRemap` is set. Because every reassignment path (connections,
  bypass, disable, animation override, hot reload) goes through the setter, the wrap can never be lost —
  same robustness as a per-invoke check, without the per-frame branch or extra cache-line touch.
  `Symbol.Child.ApplyTimeClipRemapToSiblingOutputs` sets the clip at instantiation via the internal
  `ITimeClipRemapTarget` bridge; warns on multiple `TimeClipSlot` outputs and uses the first.
- The affine map is `TimeClip.MapTimelineToSource(bars)` (guards degenerate `TimeRange`);
  `TimeClipSlot.UpdateWithTimeRangeCheck` now uses it too, replacing the unguarded `.Remap` division.
- The setter-aliasing fix: `UpdateAction = null` now clears the wrapper instead of leaving a slot that warns
  every frame.
- Sweep result: only `VideoClip` and `MidiClip` carried manual remaps — both deleted. `VideoClip`'s
  export-gate "is active" test re-expressed in source space (min/max, so a reversed clip still gates).
  `AudioClip`, `LoadMidiFile`, `LoadDataClip` confirmed clean (no `context.LocalTime` reads);
  `LoadHFCS` (example) initializes identity ranges and its `Rotation` output is *fixed* by the sibling remap.
- Builds green: Core, Video, Io, Lib, Editor (Release).
- Manual test set added: [`time-clip-evaluation.md`](../../.tests-manual/time-clip-evaluation.md).
- **Found in testing (2026-08-08), fixed:** a third evaluation path existed — `_ProcessVideoClips` pulls a
  clip's `ColorInput` / `BlendModeInput` **input slots directly with the player's context**, bypassing both
  the gated clip slot and the wrapped sibling outputs, so animated per-clip params sampled global time
  (pre-existing; Phase 2's local-time insertion made it visible). The player now remaps
  `LocalTime`/`LocalFxTime` around those pulls using the clip's `TimeClip` (which `ClassifyClip` already
  resolved). **Rule for reviewers:** any consumer that reaches into a *foreign* clip's slots with its own
  context must remap the same way — a future transition op reading its sources' params is the next candidate.
- **Found during the test-set run (2026-08-09), fixed:** export of video clips crawled at 1–3 s/frame.
  Log probes showed every new-frame request riding out the full 5 s export wait timeout and then reporting
  `ready=True`. Root cause was latent in `VideoServices` (not this plan's changes):
  `VideoPlaybackController.WaitForRequestedFrame`'s predicate accepted only the software publish flag
  (`_hasPendingFrame`) — the zero-copy path publishes via `_hasPendingGpuFrame`, which the wait never
  checked, so it always timed out. Predicate now accepts both; export requests measure 0–4 ms.
- **Open:** in-editor run-through of that test set; release-note line for the `Wake.t3`-class behaviour shift
  and the flipped `Ctrl+Alt` slip direction.

### Phase 2 — Keyframe writes go through the clip mapping

**Goal:** keys are inserted in the same time space they are sampled in.

**Scope:**

- Add a resolver that, given the composition being edited, walks the breadcrumb chain to the root and composes
  the `TimeRangeMapping`s of every enclosing clip into one affine mapping. Cache per composition; rebuild when
  the composition changes or any enclosing `TimeClip` is edited.
- Route the five write sites through it: `Animator.cs:101`, `:132`, `:491`, `:518` and
  `ChangeInputValueCommand.cs:20`. Each converts `Playback.Current.TimeInBars` → local before writing.
- Where the composition is opened without a parent path (Symbol Browser, no breadcrumb), the mapping is
  identity — document the rule rather than guessing a context.

**Testable outcome:**

- Inside a clip with `SourceRange != TimeRange`, `Alt`-clicking a parameter inserts a key that is immediately
  under the playhead and takes effect at that instant. Repeat with a 50 % speed clip and a slipped clip.
- Nested case: a clip inside a clip. Keys land correctly with both mappings composed.
- The warning note at the end of [`.help/docs/using/Timeline.md`](../../.help/docs/using/Timeline.md) can be
  deleted.

**Effort:** ~1 day. The resolver is shared with [`Plan_TimelineClipUx.md`](Plan_TimelineClipUx.md) Phase A —
build it here, consume it there.

**Status: DONE — verified in-editor 2026-08-08** (guided test set passed 8/8, including the follow-up
fixes below found during the run). As built:

- The resolver is **`Animator.GetLocalAnimationTime(Instance?, double)`** in Core — no breadcrumb needed:
  the `Instance.Parent` chain *is* the clip path. It recurses to the root and applies each ancestor's
  `ITimeClipProvider` output mapping outermost-first, **including the op's own clip** (matches evaluation:
  an op's animated inputs are read inside its own remap). Identity for instances without a parent path,
  which resolves the "opened from the Symbol Browser" rule for free.
- Routed sites (all had a concrete instance available):
  - `Animator.AddCurvesForFloatVector` / `AddCurvesForIntVector` (first-key insertion)
  - `Animator.UpdateVector3InputValue` / `UpdateFloatInputValue` (gizmo/camera writes)
  - `ChangeInputValueCommand` — new optional `Instance childInstance` ctor param (construction-time only,
    not stored, per the command Guid rules); wired at `ParameterWindow.HandleInputEditState` (the main
    slider path), `TransformGizmoHandling` (×3), `GradientUi` / `GradientSliderUi` (×4)
  - `InputValueUi.DrawAnimatedParameter` — the keyframe-toggle indicator now *queries and inserts* at local
    time, so the dot lights up when the playhead actually crosses a key inside a remapped clip
  - `DopeSheetArea` — `InsertKeyframe` / `InsertKeyframeWithIncrement` (`Shift+C`) per-parameter via
    `p.Instance`
- **Found in testing (2026-08-08), fixed — two follow-ups:**
  - **`,` / `.` keyframe navigation jumped to raw curve time.** Inside a stretched clip that parked the
    playhead where the indicator (correctly) saw no key — mixed time spaces made the toggle untestable.
    Now: `TimeClip.MapSourceToTimeline` (inverse map) + `Animator.GetGlobalAnimationTime` (composed inverse,
    mirror recursion of `GetLocalAnimationTime`); `TimeLineCanvas.HandleDeferredActions` searches each
    parameter's curves in its local time and compares/jumps in playback time. Clip starts/ends unchanged.
  - **Exact-equality key lookup breaks under remapping.** `map(playhead)` lands fractionally off the key's
    quantized U when clip ranges aren't exact (float ranges, drag snap) — the indicator missed the key and
    the toggle inserted a near-duplicate. `InputValueUi.DrawAnimatedParameter` now finds a key within
    ~1/100 bar of playback time (tolerance transformed into local space so it follows the clip rate) and
    removes *that* key rather than the mapped time. Exact behaviour for identity clips is unchanged.
- **Adjacent fix (same test session):** a Replace-mode selection fence *anywhere* in the timeline body
    began with `ClipSelection.Clear()` — fencing keyframes below a selected clip deselected that clip, and
    since dope-sheet rows are derived from the node selection, the rows (and the keys being fenced)
    vanished mid-drag. First attempt gated only `ClipArea.UpdateSelectionForArea`, which was insufficient:
    the dispatcher (`AnimationCanvas.UpdateSelectionForArea`) issues the Replace-clear via a separate
    rect-less `ClearSelection()` call on *all* manipulators. Final shape: `ITimeObjectManipulation` gained
    `ParticipatesInFence(ImRect)` (default-true DIM); the dispatcher clears/updates only participating
    manipulators; `ClipArea` participates only when the fence intersects the clip lanes.
- **Slider edits on an existing key inside a remapped clip** could insert a fractionally-offset duplicate
    instead of updating the key (`ChangeInputValueCommand` captured `_animationTime` by exact mapping).
    Now snapped at construction via `Animator.SnapToExistingKeyTime` + the shared
    `Animator.GetLocalTimeTolerance` (~1/100 bar of playback, transformed to local space) — same tolerance
    the keyframe indicator uses. Side effect (deliberate): even without clips, an edit within 0.01 bars of
    an existing key now updates that key instead of creating a near-duplicate.
- **Adjacent fix from the same test session (not clip-related):** `Shift+C`
    (`InsertKeyframeWithIncrement`) sprayed +1 keys onto *every* visible dope-sheet parameter, wrecking
    vector animations (e.g. a Color fade). Now scalar-only (`Curves.Length == 1`); plain insert-keyframe is
    unchanged. `.help/docs/using/Timeline.md` updated.
- **Deliberately left at global time:** `SymbolVariationPool` (snapshot/variation blending),
  `SnapshotControlView`, `RecordingSession`, `TimelineClipDrop`, `ProjectSettingsWindow`, `ChangeSymbol`,
  `NodeActions` — these blend or initialize values and mostly guard on non-animated inputs; snapshot-blending
  an animated parameter *inside* a remapped clip remains at global time (pre-existing semantics, revisit with
  Phase 3 if it bites).
- Builds green: Core, Editor, Lib.
- Manual test set added: [`time-clip-keyframe-insertion.md`](../../.tests-manual/time-clip-keyframe-insertion.md).
- **Open:** in-editor verify; the `.help/docs/using/Timeline.md` warning-note removal is deferred until
  Phase A lands (until the dope sheet *displays* in local time, inserted keys can sit visually away from the
  playhead inside a remapped clip even though they play correctly — removing the warning now would be
  premature).

### Phase 3 — Per-curve time space (`Local` | `Global`)

**Goal:** an explicit escape hatch for "this animation happens at an absolute timeline moment regardless of
where the clip sits."

**Scope:**

- Add a `TimeSpace { Local, Global }` to the animation entry (per `(childId, inputId)` group — **not** per
  component curve; all components of a vector share one space).
- In `Animator.CreateUpdateActionsForExistingCurves`, branch the sampled time:
  `Local` → `ctx.LocalTime` (today's behaviour, the default), `Global` → `ctx.Playback.TimeInBars`.
  Keep the closures allocation-free; pick the accessor once when building the closure, not per frame.
- Serialize on the Animator entry alongside `InstanceId` / `InputId` / `Index`
  ([`Animator.Write`](../../Core/Operator/Animator.cs#L371)). Omit when `Local` so existing files are untouched
  and diffs stay small.
- Phase 2's write resolver honours the flag: a `Global` curve inserts at `Playback.TimeInBars` unmapped.
- Parameter-window affordance to flip it, plus an indicator in the dope sheet — specified in
  [`Plan_TimelineClipUx.md`](Plan_TimelineClipUx.md).

**Testable outcome:**

- Two identical animated parameters inside one clip, one `Local` and one `Global`. Dragging the clip moves the
  first and leaves the second where it was. Reload the project: both keep their space.
- An older `.t3` with no `TimeSpace` field loads as `Local` and behaves exactly as before.

**Effort:** ~0.5–1 day once Phases 1 and 2 are in.

### Phase 4 — `SourceRangeBehaviour` replaces the marker interfaces

**Goal:** one declaration describing how `SourceRange` responds to move and trim, including the
currently-missing normalized case.

**Current encoding, spread across three places:**

- [`IPreventingTimeRemap`](../../Core/Operator/Slots/TimeClipSlot.cs#L33) — implemented only by
  [`[TimeClip]`](../../Operators/Lib/flow/TimeClip.cs#L4)
- [`IContentTimeClip`](../../Core/Operator/Slots/TimeClipSlot.cs#L41) — `[VideoClip]`, `[AudioClip]`; read at
  [`AddSymbolChildCommand.cs:56`](../../Editor/UiModel/Commands/Graph/AddSymbolChildCommand.cs#L56) to default
  `SourceRange` to `[0, duration]` instead of `SourceRange = TimeRange`
- [`TimeClip.UsedForRegionMapping`](../../Core/Animation/TimeClip.cs#L40) — derived from `IPreventingTimeRemap`
  at [`TimeClipSlot.cs:56`](../../Core/Operator/Slots/TimeClipSlot.cs#L56), consumed by the drag logic at
  [`TimeClipInteractions.cs:479`](../../Editor/Gui/Windows/TimeLine/TimeClips/TimeClipInteractions.cs#L479)

**Proposed:**

```
RegionMapping     // [TimeClip] — dragging moves TimeRange and SourceRange together;
                  // the subtree keeps parent time
ContentAnchored   // [VideoClip], [AudioClip] — SourceRange is content-time and stays put;
                  // dragging re-times placement only  (today's default for everything else)
Normalized        // SourceRange fixed (typically [0,1] bars); trimming always stretches
                  // the content to fit. The reusable-transition case.
```

**Scope:**

- Declare it once on the symbol (attribute or interface), resolve to a field on `TimeClip` at
  `SetOutputData`, keep `UsedForRegionMapping` as a back-compat computed property until callers migrate.
- `Normalized` changes two interaction defaults, both currently reachable only via `Alt`:
  - body drag → `TimeRange` only (`SourceRange` never follows)
  - trim → `SourceRange` fixed, so the content stretches
    ([`UpdateDragAtStartPointCommand`](../../Editor/Gui/Windows/TimeLine/TimeClips/TimeClipInteractions.cs#L494)
    currently captures the rate before mutation specifically to *preserve* speed; `Normalized` skips that)
- `Alt` keeps inverting whatever the declared behaviour is, so the existing muscle memory still works.
- Serialization: the enum is derived from the symbol, not stored per clip — no `.t3` change.

**Testable outcome:**

- A symbol declared `Normalized` with `SourceRange = [0,1]` and keyframes across that bar: dropped on the
  timeline and stretched from 1 bar to 4, the animation plays over 4 bars. Squashed to half a bar, it plays in
  half a bar. No `Alt` needed.
- `[TimeClip]` and `[VideoClip]` drag/trim exactly as before.

**Effort:** ~1 day. Unblocks the transition operator in
[`Plan_TimelineClipUx.md`](Plan_TimelineClipUx.md) Phase D.

## Open questions

1. **Is Phase 1 in scope for 4.3? — RESOLVED 2026-08-08: yes, with a release note.** A scan of ~4,700 `.t3`
   files (repo `Operators/` + the user's `Documents/TiXL*` project folders) found exactly **one** affected
   curve set: a `Color.W` fade on a `[VideoClip]` in `Wake.t3`, whose clip is slipped by 0.037 bars (~70 ms,
   rate 1.0) — the fade shifts imperceptibly and is trivially re-nudged. MidiClip double-remap exposure: zero
   files. No grandfathering machinery; document the behaviour change in the release notes.
2. **Where does the composed mapping stop at a `[SetTime]`?** `[SetTime]`
   ([`SetTime.cs:30-47`](../../Operators/Lib/numbers/anim/time/SetTime.cs#L30)) also writes `LocalTime`, and
   `GlobalAbsolute` does not restore it — so evaluation order decides which siblings observe the override. Any
   editor-side mapping is therefore a best-effort reconstruction of the clip chain, not ground truth. Proposal:
   the resolver ignores `[SetTime]` entirely and the UX plan surfaces a "time is overridden in this subtree"
   badge. Confirm that is acceptable rather than trying to model it.
3. **Should `Global` mean root time or parent-clip time?** Proposal: root (`Playback.TimeInBars`), because it
   is already on the context and needs no resolution. A three-way `Local | Parent | Root` is almost certainly
   over-engineering, but worth a moment before the enum is serialized.
4. **`ImageSequenceClip`** is a symbol op with a `TimeClipSlot<Command>`; confirm during the Phase 1 sweep that
   it has no hidden time assumptions.
5. `TimeClipSlot.InvalidationOverride` short-circuits on `HasInputConnections`
   ([`TimeClipSlot.cs:143`](../../Core/Operator/Slots/TimeClipSlot.cs#L143)), skipping the `Suspended` check for
   composition outputs. Probably harmless; note it during Phase 1 rather than fixing opportunistically.

## Documentation

- [`.help/docs/using/Timeline.md`](../../.help/docs/using/Timeline.md) — rewrite the "Time remapping" section:
  animation inside a clip runs in clip time (Phase 1), keys land where you insert them (Phase 2), the closing
  warning note is removed. Add the per-parameter time space (Phase 3) and the normalized-clip behaviour
  (Phase 4).
- Operator descriptions for `[VideoClip]` and `[MidiClip]` — edited **in the editor**, not in the generated
  `.md` — should stop implying that the consumer's choice of output affects timing.

## Manual test sets

- `time-clip-evaluation.md` — Phase 1. Same clip consumed through both outputs, video and MIDI, scrub +
  export. Frontmatter: `added: 2026-08-06`, `added-in-version: 4.3`.
- `time-clip-keyframe-insertion.md` — Phase 2. Insert keys in identity, stretched, slipped, and nested clips.
- `time-clip-time-space.md` — Phase 3. Local vs Global on two identical parameters; project round-trip.
- `time-clip-normalized.md` — Phase 4. Stretch and squash a normalized clip; verify `Alt` still inverts.
- Regression net: the existing `video-clip-player-wired.md`, `video-clip-player-autocollect.md`,
  `timeline-audio-clips.md` and `dataclip-editing.md` must all still pass unchanged after Phase 1.
