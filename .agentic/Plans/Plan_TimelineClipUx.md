# Timeline clip UX (local/global display, generalized clips, transitions)

**Status:** Drafted 2026-08-06. The *editor* half of a two-plan pair; the evaluation-model half is
[`Plan_TimeClipEvaluation.md`](Plan_TimeClipEvaluation.md). Phase A depends on that plan's Phase 2 (the
composed clip mapping); Phase D depends on its Phase 4 (`Normalized` clips). Phases B and C are independent
and can start immediately.

## Goal

Bring TiXL closer to non-linear video editing without inventing a second timeline model:

1. The dope sheet can show keyframes either in the clip's own time or in the surrounding timeline's time,
   under an explicit toggle — so a reusable clip can be authored in isolation *and* timed against the
   soundtrack.
2. Any clip that produces a texture composites through one path, not just `[VideoClip]` — so procedural and
   generated clips are first-class.
3. Transitions between clips are ordinary operators with two texture inputs, placed on the timeline like any
   other clip.

## Current state — what exists

### The Global/Local idea is already in the file, commented out

[`TimeLineCanvas.cs:401-449`](../../Editor/Gui/Windows/TimeLine/TimeLineCanvas.cs#L401) holds a disabled
`UpdateScaleAndTranslation(compositionOp, ScalableCanvas.Transition)` that reads
[`Structure.GetCompositionTimeClip`](../../Editor/UiModel/ProjectHandling/Structure.cs#L121) and, on `JumpIn`,
maps `clip.TimeRange` → `clip.SourceRange`, writing `ScaleTarget` / `ScrollTarget` — the damped targets, so it
animates rather than pops.

It solves **framing** (keeping the view anchored when you enter a clip), not **agreement**: it scales the whole
canvas, so the playhead — drawn at global `Playback.TimeInBars` — moves with it and still will not sit on the
right key. Both are worth having; they are different fixes.

### What the timeline knows and does not know

- [`DopeSheetArea.cs`](../../Editor/Gui/Windows/TimeLine/DopeSheetArea.cs) contains **no** reference to
  `TimeClip` or `SourceRange`. Keyframe rows are drawn at raw curve `U`.
- The playhead is never rebased. `Structure.GetCompositionTimeClip` is not called by the timeline at all.
- [`ClipRange.cs`](../../Editor/Gui/Windows/TimeLine/ClipRange.cs) is the one place the composition's *own*
  clip surfaces: it shades outside `SourceRange` and, with `Alt`, offers handles that edit it. Good precedent
  for the visual language; it is a static overlay, not a coordinate change.
- [`TimelineState`](../../Editor/Gui/Windows/TimeLine/TimelineState.cs) already persists per-symbol view state
  (`ScaleX`, `ScrollX`, `Mode`, heights) into the `.t3ui` settings block, with an established back-compat
  read pattern.
- [`GetAnimationParametersForSelectedNodes`](../../Editor/Gui/Windows/TimeLine/TimeLineCanvas.cs#L842) walks
  `compositionOp.Children` only, so the parameter list is already composition-scoped. Its own comment says it
  should be refactored; do not expand it in this plan.

### `Combine as time clip` has never worked

[`CombineToSymbolDialog.cs:45`](../../Editor/Gui/Graph/Dialogs/CombineToSymbolDialog.cs#L45) offers the
checkbox and passes it to `Combine.CombineAsNewType`, which accepts it at
[`Combine.cs:21`](../../Editor/UiModel/Modification/Combine.cs#L21) and never reads it —
[`Combine.cs:99`](../../Editor/UiModel/Modification/Combine.cs#L99) always emits `"Slot<"`. Git history:
`718318a54 "Add stub for Combined symbol should be a timeclip"`, March 2021. It has been a stub since the day
it was added.

The working path is **Add Output → "Is time clip"**
([`AddOutputDialog.cs:40`](../../Editor/Gui/Graph/Dialogs/AddOutputDialog.cs#L40) →
[`InputsAndOutputs.cs:267`](../../Editor/UiModel/Modification/InputsAndOutputs.cs#L267)), which does emit
`TimeClipSlot<`.

### The compositor is narrower than its own interface

[`IVideoClipProvider`](../../Operators/Video/lib/io/video/VideoClip.cs#L14) is:

```csharp
internal interface IVideoClipProvider
{
    Slot<Texture2D>    TextureOutput  { get; }
    InputSlot<Vector4> ColorInput     { get; }
    InputSlot<int>     BlendModeInput { get; }
    void MarkManaged();
}
```

Nothing in it is video-specific — it describes *a clip that yields a texture with a tint and a blend mode*.
It is referenced only in `VideoClip.cs` and
[`_ProcessVideoClips.cs`](../../Operators/Video/lib/io/video/_ProcessVideoClips.cs) (`:23`, `:26`, `:59`, `:85`,
`:95`).

Separately, [`TimeClipItem.cs:83`](../../Editor/Gui/Windows/TimeLine/TimeClips/TimeClipItem.cs#L83) and `:723`
hard-code the `VideoClip` symbol Guid to decide whether to draw thumbnails, so no other clip type can ever
show a preview.

### Transition primitives that already exist

- [`BlendImages`](../../Operators/Lib/image/use/BlendImages.cs) `48781d5a-…` — `BlendFraction:float` over a
  `MultiInputSlot<Texture2D>`. The closest thing to a cross-fade today.
- [`Blend`](../../Operators/Lib/image/use/Blend.cs) `9f43f769-…` — `ImageA`/`ImageB` + RGB/alpha blend modes +
  `ScaleMode`.
- [`BlendWithMask`](../../Operators/Lib/image/use/BlendWithMask.cs) — A/B plus a mask texture; the wipe shape.
- `BlendScenes` for `Command`s.
- [`[TimeClip]`](../../Operators/Lib/flow/TimeClip.cs#L21) already publishes
  `context.FloatVariables["_normalizedTime"]` = 0..1 across the clip. The progress convention exists.

There is **no** `CrossFade` / `Dissolve` / generic transition operator, and `_ProcessVideoClips` has no
cross-fade — it stacks clips with per-clip `Color` and `BlendMode`, one `DrawScreenQuad` each.

## Architectural decisions (proposed — confirm before Phase A)

- **Two coordinate systems, one mapping, both directions.** When editing inside a clip there is source time
  `u` (where keys live) and parent time `t` (where the playhead and soundtrack live), related by the composed
  `TimeRangeMapping` from [`Plan_TimeClipEvaluation.md`](Plan_TimeClipEvaluation.md) Phase 2. The toggle picks
  which one the canvas is in:

  | Mode | Canvas is in | Playhead drawn at | Keys drawn at | Soundtrack / bar raster |
  | --- | --- | --- | --- | --- |
  | **Local** | source time | `LocalBarsToSourceBars(t)` | raw `u` | inverse-mapped, or hidden |
  | **Global** | parent time | `t` | `SourceBarsToLocalBars(u)` | lines up naturally |

  Today's behaviour is neither: the canvas is in source time but the playhead is drawn at `t`, which is why
  keys and playhead disagree inside a remapped clip.

- **Per-row mappings are the primary case, not the exception.** (Revised 2026-08-08 — the original "one
  mapping for the whole dope sheet" assumption was disproved by the 4.3 test sessions.) The dominant
  misalignment is *clip ops' own animated parameters viewed from the parent composition* — e.g. a
  `[VideoClip]`'s `Color` fade: its curves live in the clip's source time while the surrounding canvas is in
  parent time, and two clips on one timeline have two different mappings. The per-instance resolver pair
  (`Animator.GetLocalAnimationTime` / `GetGlobalAnimationTime`, built in
  [`Plan_TimeClipEvaluation.md`](Plan_TimeClipEvaluation.md) Phase 2) composes the full chain per row and
  covers both this case and the entered-nested-clip case with one code path — `,`/`.` navigation, the
  keyframe indicator, and insertion already use it. Phase A therefore splits:
  - **A1 — per-row alignment (no toggle):** `DopeSheetArea` draws each row's keys at
    `GetGlobalAnimationTime(p.Instance, key.U)` and inverts drag / fence / snap / SRI through
    `GetLocalAnimationTime`. In the parent view there is nothing to toggle — correct is correct. This
    removes the "keys draw at raw curve time" caveat from the 4.3 test sets.
  - **A2 — the Global/Local toggle** for *entered* clip compositions (ruler rebasing, soundtrack, damped
    framing, `TimelineState` persistence) — the remainder of the original Phase A design below.

- **Editing inverts through the same mapping.** Dragging a key at screen x → `t` → `u` via
  `LocalBarsToSourceBars`. Key insertion uses the same call, so the toggle and
  [`Plan_TimeClipEvaluation.md`](Plan_TimeClipEvaluation.md) Phase 2 are the same code path — build it once.

- **Nesting composes.** Chain the mappings up the breadcrumb; `Global` means root time. Consistent with that
  plan's open question #3.

- **Framing and agreement stay separate.** Revive the commented `UpdateScaleAndTranslation` for the smooth
  jump-in/out framing *in addition to* the mode work, not instead of it. It already writes the damped targets,
  so entering a clip should ease rather than jump.

- **Mode is persisted per symbol** in `TimelineState`, next to `Mode` / `ScaleX` / `ScrollX`. This lets a
  reusable clip be authored in `Local` while the outer edit stays in `Global`. Default `Global`, because it
  matches what users see before they enter anything and it is the mode in which keys and playhead agree with
  the soundtrack. *(Confirm — see Open questions.)*

- **The toggle is inert outside a clip.** When the composed mapping is identity, `Local` and `Global` are the
  same view; show the control disabled rather than hiding it, so it does not appear and disappear.

- **`IVideoClipProvider` → `ITextureClipProvider`, moved out of the `Video` package.** The compositor should
  not know what produced the texture. Placement needs care: `Video` cannot be a dependency of `Lib`. See Open
  questions #4.

- **A transition is a clip, not a property of a cut.** There is no symbol-reference input type anywhere in the
  codebase, so "this cut uses transition X" would need new infrastructure. A clip-shaped transition op needs
  none, appears on the timeline for free, and gets progress and local animation from the existing remap.

## Phases

### Phase A — Global / Local display mode

**Goal:** inside a remapped clip, keys, playhead, and soundtrack agree — in whichever of the two spaces the
user picks.

**Scope:**

- Consume the composed mapping resolver from
  [`Plan_TimeClipEvaluation.md`](Plan_TimeClipEvaluation.md) Phase 2.
- Add `ClipTimeDisplay { Global, Local }` to
  [`TimelineState`](../../Editor/Gui/Windows/TimeLine/TimelineState.cs), serialized with the existing pattern
  and defaulted so older `.t3ui` files read as the chosen default.
- Apply the mapping in `DopeSheetArea` when drawing keyframe positions, and invert it in the drag/insert paths
  (`AnimationParameterEditing`, `KeyframeCopyAndPasting`, the snap attractors). Every place that converts
  between canvas U and curve U goes through one helper — do not scatter the math.
- Ruler, raster, and `TimeLineImage` (soundtrack background) follow the active space.
- Toolbar control next to the existing Dope/Curve switch in
  [`TimeControls.cs:553`](../../Editor/Gui/Windows/TimeLine/TimeControls.cs#L553). Use
  `CustomComponents.IconButton` with `ButtonStates.Activated` for the engaged state, inside the toolbar's
  `PushToolbarIconBackground()` cluster. `UiColors.StatusAnimated` (orange = time-related) for any indicator
  tint. Disabled when the mapping is identity.
- Revive `UpdateScaleAndTranslation` ([`TimeLineCanvas.cs:401-449`](../../Editor/Gui/Windows/TimeLine/TimeLineCanvas.cs#L401))
  for damped framing on enter/leave, and on mode switch so the two spaces cross-fade instead of snapping.
- While here, fix the adjacent mode-switch bug: `TimeControls.cs:558` cycles the `Modes` enum modulo 3 and
  transiently lands on `Modes.Undefined`, which `UpdateMode` then coerces back.

**Explicitly not in scope:** per-row mappings, showing a clip's interior from outside, non-affine retiming.

**Design principle (from 4.3 test sessions): row stability.** Dope-sheet rows are a live projection of the
node selection, so any interaction that mutates selection mid-gesture can remove the very rows being
operated on (found concretely: a Replace-fence cleared the clip selection and the keyframe rows vanished
mid-drag — spot-fixed by lane-gating the clip fence). Phase A work must uphold: *an interaction in progress
never removes the rows it operates on.* Candidate mechanism: freeze the parameter list while a fence/drag
gesture is active, or keep rows alive while any of their keyframes are selected. The "Keep animated
parameters visible" pin is the existing user-side workaround.

**Testable outcome:**

- Enter a clip with `SourceRange != TimeRange`. In `Global`, keys sit under the playhead at the moment they
  take effect and align with the soundtrack waveform; scrubbing, key dragging, fence selection, and snapping
  all land where the cursor is. In `Local`, the same keys sit at their authored positions and the ruler counts
  the clip's own bars.
- Switching modes eases rather than jumps; entering and leaving a clip does the same.
- A 50 % speed clip and a slipped clip both behave.
- Outside any clip, both modes are identical and the control reads as disabled.
- Mode survives save/reload, per symbol.

**Effort:** ~2–3 days. The mapping is cheap; the cost is finding every canvas-U ↔ curve-U conversion in the
dope-sheet interaction code and routing it through one helper.

**Interaction risk:** this changes what the ruler means. Worth a design round-trip on the visuals before
implementation — see Open questions #1.

**A1 status: code done (2026-08-08), pending in-editor verify** — as built:

- `TimeLineCanvas.ParamTimeMapping` — per-parameter affine snapshot (`global = offset + rate·u`, built from
  the composed resolvers, identity fast-path, degenerate-rate guard). Built per row per frame; the ~2×
  chain-walk per parameter is negligible.
- **Converted to playback space:** `DopeSheetArea` row drawing (keyframe icons, constant-value labels,
  curve polylines via a local-space visible window + rate-scaled sample density, 4-component gradients,
  hover-insert preview, click-sets-playhead, FrameStats before/after), fence selection (per-row local
  window, min/max-swapped for negative rates), key dragging (drag origin + per-row `dt / rate` in
  `UpdateDragCommand`), stretch + selection/all-keys time ranges (base methods made virtual, overridden —
  so the SRI operates in playback space), the snap attractor, `TimeSelectionArea` (bucket positions,
  cluster drag via parallel per-key mappings, cache hash includes the mapping so clip drags refresh dots),
  and `TimeWarpDrag` (snapshots playback positions, writes back through each key's mapping; clip retiming
  unchanged).
- New pipes: `AnimationParameterEditing.EnumerateKeyframesWithMapping`,
  `KeyframeEditorGroup.EnumerateKeyframesWithMapping` + `ApplyKeyframePlaybackTimeOffset`.
- **Still raw (documented, deferred):** the fullscreen **Curve** mode and the inline curve pane
  (internally consistent, but their SRI union can drift when open over a remapped row), keyframe
  copy/paste offsets, `Duplicate`, and `ViewAllOrSelectedKeys` framing bounds. These follow the same
  recipe when picked up.
- Manual test set: [`timeline-clip-time-display.md`](../../.tests-manual/timeline-clip-time-display.md);
  the raw-time caveats in `time-clip-keyframe-insertion.md` were replaced with playback-position
  expectations (step 6 now asserts keys draw at bars 3/5, not 1.5/2.5).
- **Found in first blind test (2026-08-08), fixed:**
  - *Fence dead strip:* `ParticipatesInFence` used `LastHeight`, which carries ~5 px bottom padding —
    a fence in that strip cleared the clip selection (rows vanished) without being able to select
    anything. Now uses the exact drawn-lane rect (`_lanesScreenTop/Bottom`, reset to an empty band when
    no clips exist).
  - *Clip-drag snap jitter:* keyframe snap anchors are published in playback time, so keys riding on a
    dragged clip moved **with** the drag — the clip snapped toward its own keys, oscillating 0–6 px.
    While `TimeClipInteractions.IsDraggingClips`, the dope sheet's snap attractor now skips parameters
    whose instance chain contains a selected clip.
  - *Splitting an animated clip threw* `EnumFailedVersion` — `Animator.CopyAnimationsTo` inserts into
    the dict it enumerates when source and target animator are the same object (copy within one
    composition). Pre-existing; surfaced because animating clip params is now the natural workflow.
    Fixed by collect-then-insert (Core).

### Phase B — Make `Combine as time clip` real

**Goal:** one action turns a selection into a reusable clip symbol.

**Scope:**

- Honour `shouldBeTimeClip` at [`Combine.cs:99`](../../Editor/UiModel/Modification/Combine.cs#L99), emitting
  `TimeClipSlot<` for the primary `Command` output, matching what `InputsAndOutputs.cs:267` already does.
- Support the **two-output shape** a procedural clip needs: `TimeClipSlot<Command>` *and* `Slot<Texture2D>`.
  This is what Phase C composites and Phase D builds on.
- Initialize the new clip's `TimeRange` / `SourceRange` sensibly — reuse
  [`AddSymbolChildCommand.InitContentClipSourceRange`](../../Editor/UiModel/Commands/Graph/AddSymbolChildCommand.cs#L50)
  rather than adding a second convention.
- Dialog copy: say what the checkbox does. Today it silently does nothing.

**Testable outcome:**

- Select a few ops that render to a texture, Combine as time clip. The new symbol appears as a clip on the
  timeline, can be dragged, trimmed, and layered, and its contents evaluate only inside its `TimeRange`.
- Keyframes authored inside it move with the clip (this is the payoff from
  [`Plan_TimeClipEvaluation.md`](Plan_TimeClipEvaluation.md) Phase 1).
- Undo restores the pre-combine graph.

**Effort:** ~1 day.

**Status: code done (2026-08-08), pending in-editor verify** — as built:

- `Combine.CombineAsNewType` honours `shouldBeTimeClip`: the **first `Command`-typed output** becomes the
  `TimeClipSlot` (falling back to the first output of any type), matching the slot-string emission of
  `InputsAndOutputs.AddOutputToSymbol`. Other outputs stay plain `Slot<>` — the two-output shape
  (`TimeClipSlot<Command>` + `Slot<Texture2D>`) falls out naturally when the selection has both connection
  kinds, and Evaluation Phase 1 remaps the sibling outputs.
- A selection with **no outgoing connections** gets a default unconnected `TimeClipSlot<Command> Output`,
  so the combined symbol can still appear on the timeline instead of silently producing a non-clip.
- Dialog: tooltip on the checkbox explaining what a time clip means (it previously did nothing and said
  nothing).
- Clip range init is the `TimeClip` ctor default (`TimeRange = SourceRange = [playhead, +4 bars]`, i.e.
  identity mapping). **Undo note:** combining already clears the undo stack (pre-existing; symbol/assembly
  creation can't be cleanly undone) — the "undo restores the pre-combine graph" outcome above is therefore
  not achievable and was wrong in the draft; verify the hint text in the dialog covers it.
- **Deferred polish:** initialize the clip's ranges from the copied keyframes' extent instead of
  playhead+4, so combining an already-animated selection produces a clip that plays its animation without
  manual range adjustment.
- Builds green (Editor).

### Phase C — Generalize the compositor to texture clips

**Goal:** anything that yields a texture on the timeline composites through one path.

**Scope:**

- Rename `IVideoClipProvider` → `ITextureClipProvider` and relocate it so non-video packages can implement it
  (see Open questions #4). Update the five reference sites in `_ProcessVideoClips.cs` and the four in
  `VideoClip.cs`.
- Consider renaming `_ProcessVideoClips` / `VideoClipPlayer` to match, weighed against
  [`Plan_VideoClipPlayer.md`](Plan_VideoClipPlayer.md) Phase 4's still-open input-naming decision and the fact
  that `VideoClipPlayer` is already in user projects. Symbol Guids are preserved either way.
- Replace the hard-coded Guid check at
  [`TimeClipItem.cs:83`](../../Editor/Gui/Windows/TimeLine/TimeClips/TimeClipItem.cs#L83) / `:723` with a
  capability the clip declares, so procedural clips can supply a preview. Video keeps its existing
  `VideoClipThumbnailCache` path as one implementation.
- Verify the auto-collect sibling scan and its `Symbol.VersionCounter` cache still behave when the scanned set
  is heterogeneous.

**Testable outcome:**

- A procedural clip built in Phase B and a `[VideoClip]` sit on adjacent layers and composite together, with
  per-clip color and blend mode honoured on both, wired and auto-collected.
- Existing video projects are visually unchanged.
- The "not drawn by any player" status hint still fires for both clip kinds.

**Effort:** ~1–1.5 days, mostly mechanical once the interface placement is settled.

### Phase D — Transition clips

**Goal:** a transition is an operator with two texture inputs, placed and stretched on the timeline.

**Scope:**

- A clip-shaped transition op, declared `Normalized`
  ([`Plan_TimeClipEvaluation.md`](Plan_TimeClipEvaluation.md) Phase 4) so stretching it stretches the
  transition:

  ```
  [VideoTransition]                     // name TBD — see Open questions #5
    Output : TimeClipSlot<Texture2D>    // placeable + stretchable on the timeline
    ImageA / ImageB : Texture2D         // outgoing / incoming
    Mix             : Texture2D         // the two-texture op that does the work
  ```

  Progress comes from the existing `_normalizedTime` convention, so any of `BlendImages`, `Blend`,
  `BlendWithMask`, or a user-authored shader can be wired into `Mix`. Because the clip remaps local time, any
  keyframed parameter in that subgraph animates across the transition.
- **Claiming.** While a transition is active the player must not also draw its two sources. Reuse the existing
  frame-stamp mechanism (`MarkManaged` / `Playback.FrameCount`, already used for the "not drawn by any player"
  hint) inverted: the transition stamps its sources, the player skips anything stamped this frame.
- **Insert-transition command.** Creates the op, wires both neighbours, sets `TimeRange` to their overlap (or
  a default duration centred on the cut), and places it on a free layer. Model on
  [`SplitClipsAtTime`](../../Editor/Gui/Windows/TimeLine/TimeClips/TimeClipInteractions.cs#L319), which already
  does clip-copy, `SourceRange` math, and multi-input rewiring. Undoable as one `MacroCommand`.
- Consider whether the first shipped transition is a C# op at all, or a symbol op assembled from `[TimeClip]` +
  `BlendImages` — the way `VideoClipPlayer` itself was built.
- Decode: both sources must stay warm across the overlap.
  [`Plan_VideoClipPlayer.md`](Plan_VideoClipPlayer.md) open question 5 already reserves ≥2 pool slots for a
  cut; confirm rather than re-solve.

**Testable outcome:**

- Two overlapping video clips plus a transition: the output cross-fades over the transition's `TimeRange`, and
  neither source draws on its own during the overlap.
- Stretching the transition stretches the fade; no `Alt` needed.
- Swapping `Mix` from `BlendImages` to `BlendWithMask` turns the fade into a wipe with no rewiring of A/B.
- Export is frame-exact across the transition.
- Undo of the insert-transition command restores the two clips untouched.

**Effort:** ~2–3 days, most of it in claiming and the insert command.

## Follow-up design: splitting clips vs. keyframes (2026-08-08)

Splitting an animated clip copies the **full** curve set onto both halves. That is the correct data model,
not a bug: each half's `SourceRange` gates what is sampled, keys outside a half's window still shape the
interpolation approaching the cut, and playback across the split is bit-identical with zero curve
modification. Truly *splitting* the curves is not feasible in the current key model — spline segments can't
be subdivided exactly with angle+tension tangents, and key insertion recomputes neighbor tangents.

The real problem is presentation: dead keys (outside the clip's source window) are drawn full-strength and
are editable, which reads as waste/confusion.

**Decided (2026-08-08): the one meaningful offering is an explicit "Remove Unused Keyframes" action** on
the clip context menu, applying to the selected clips (or all when none selected), **disabled when there is
nothing to remove**. It deletes keys strictly outside the source window but keeps one boundary key beyond
each edge so the interpolation into the window is preserved; undoable; never done silently by the split.
Display treatments (dimming out-of-window keys, selection exclusion) were considered and dropped.

**Deferred** — bundle with a future clip/curve tools pass alongside rebuild/optimize-curve, quantize, etc.

## Follow-up design: SourceRegionIndicator placement (2026-08-08, open)

The footage/source region in the ruler (slip-drag, clip-boundary-only snapping) is working well and is a
distinctive UI concept — **manual tests and `.help/` docs are deliberately deferred** until its interaction
settles; more UX ideas are expected.

Ideas considered:

- **Move it into the keyset strip — REJECTED (2026-08-08):** the strip already carries dot-click /
  cluster-drag / **fence selection** in a ~10 px band; adding slip-drag zones would recreate the
  hit-competition just resolved in the ruler. The rename happened anyway: `TimeSelectionArea` →
  **`KeySetStrip`** (file, class, ImGui ids, cross-references), and the docs now call it the
  "keyset strip" (individual dots remain "keyset indicators").
- **Dedicated thin lane between strip and clip lanes**, shown only while a media clip is hovered/selected —
  still open; no hit competition, tightest adjacency, but a conditional row that must appear/disappear
  smoothly.

Recent tweaks already applied: flush with ruler bottom (cursor-flicker fix), ruler 28→33 px, taller grab
band above the SRI, idle outline 0.125→0.17, snapping reduced to clip-boundary alignment only
(nearest-wins, `Shift` bypasses).

1. **Visuals for the mode toggle.** This changes the meaning of the ruler, which is exactly the kind of
   discontinuity that reads as a glitch. Worth sketching before implementation: is it a two-state toggle in
   the toolbar, a segmented control, or a label on the ruler itself? Does the ruler show an explicit
   "clip time" affordance in `Local`? A design round-trip here is cheaper than an implementation round-trip.
2. **Default mode: `Global` or `Local`?** The plan assumes `Global` (keys, playhead, and soundtrack agree;
   matches the view before entering). `Local` is arguably better for authoring reusable clips. Since the
   setting is per symbol, the default mostly matters for first entry into an existing clip.
3. **Does the toggle belong to the timeline or to the composition?** `TimelineState` is per symbol, which
   means a clip symbol reused in two places shares one setting. Probably right, but worth naming.
4. **Where does `ITextureClipProvider` live?** `Video` cannot be a dependency of `Lib`, and `Core` should stay
   minimal per the project's own guidance. Candidates: a small shared operator-side contract assembly, or
   `Core` on the grounds that clip compositing is genuinely cross-package. **Needs a decision before Phase C.**
5. **Naming.** `VideoTransition` is wrong once clips are generic — `TransitionClip` or `TextureTransition`
   read better. Settle before the op ships and its input set locks. Same conversation as
   [`Plan_VideoClipPlayer.md`](Plan_VideoClipPlayer.md) Phase 4's `AutoCollect` / `IncludeTimelineClips`
   decision; do them together.
6. **Multiple transitions overlapping the same clip** (a clip that is both the incoming source of one
   transition and the outgoing source of the next). Claiming must handle a clip stamped by two transitions in
   one frame. Probably fine — it simply is not drawn on its own — but needs a test.
7. **`[SetTime]` inside a clip** makes the composed mapping a best-effort reconstruction. The proposal in
   [`Plan_TimeClipEvaluation.md`](Plan_TimeClipEvaluation.md) open question #2 is a "time is overridden here"
   badge in the dope sheet; that badge is this plan's work if accepted.

## Follow-up design: single-output media clips? (2026-08-09, open)

The two-output shape of media clips (`TimeClipSlot<Command>` + `Slot<Texture2D>`) is historic
(command-flow scene timelines), not a type-system necessity — `TimeClipSlot<Texture2D>` is legal and used
elsewhere (`[MidiClip]`'s `Dict<float>`). The only substantive difference the second output encodes is the
**gate**: `TimeClipSlot` skips evaluation outside `TimeRange`, while the texture path must stay pullable
out-of-range (decoder preroll, first/last-frame clamp) — which is exactly why Evaluation Phase 1 made
sibling outputs "remap without gate".

Candidate simplification: give `TimeClipSlot` an opt-out of the out-of-range gate (`EvaluateOutsideRange`:
remap only — the op keeps doing its own source-range clamping, as `VideoClip` already does). Then
`[VideoClip]` collapses to a single `TimeClipSlot<Texture2D>` and drops the vestigial Command in/out;
Combine-as-time-clip of texture selections emits **one** output (revising Phase B's "two-output shape");
`ITextureClipProvider` (Phase C) simplifies. Surfaced by user confusion in testing: "what does a time-clip
output of a texture op even mean?" — the current shape invites meaningless wiring.

**Migration assessment (2026-08-09): effectively none.** `[VideoClip]` shipped this cycle; known usage is
~2 placements the user can recreate — stale `.t3` output entries load with a warning and a default
placement. `[AudioClip]` is explicitly **excluded**: it has real usage, and
[`Plan_TimelineAudioClips.md`](Plan_TimelineAudioClips.md) supersedes the op with first-class timeline
audio clips anyway — reshaping a to-be-deleted op is waste. Downstream consumers are unaffected by the
`VideoClip` change: timeline discovery and the player classify by scanning outputs for `ITimeClipProvider`,
and `TimeClipSlot<Texture2D>` *is a* `Slot<Texture2D>` for wiring/interface purposes.

**Sequencing: do this before Phase C locks `ITextureClipProvider`.** The window where it costs nothing is
now.

**Status: DONE (2026-08-09)** — as built:

- `TimeClipSlot<T>.EvaluateOutsideRange` (Core): opts out of the out-of-range gate; the slot always
  evaluates and only remaps. The op keeps clamping to its source range itself.
- `[VideoClip]` is now a **single-output op**: `Texture` is a `TimeClipSlot<Texture2D>` (Guid kept,
  `DirtyFlagTrigger.Animated`, `EvaluateOutsideRange = true`). The `TimeSlot` output and the vestigial
  `Command` input were removed from `.cs` / `.t3` / `.t3ui`. No consumer changes were needed: discovery
  and the player scan outputs for `ITimeClipProvider`, and `TimeClipSlot<Texture2D>` *is a*
  `Slot<Texture2D>` for wiring and `IVideoClipProvider`.
- Builds green: Core, Video, Lib, Io, Editor. Existing placements referencing the removed output load
  with a warning and a default placement (accepted — see migration assessment).
- **Found in testing (2026-08-09), fixed — disable/re-enable:**
  - *Re-enable dead:* `TimeClipSlot.SetDisabled` (and `TransformCallbackSlot`'s) stashed the update
    action but not the dirty-flag trigger; the base `RestoreUpdateAction` then "restored" the never-set
    field, wiping `DirtyFlagTrigger.Animated` so the slot never re-evaluated. Latent forever; armed by
    the texture output becoming the TimeClipSlot. Both overrides now stash the trigger
    (`_keepDirtyFlagTrigger` widened to `private protected`).
  - *Disable froze the frame instead of hiding the clip:* the player composited the stale `Value`.
    `_ProcessVideoClips.ClassifyClip` now treats a disabled clip slot as `Inactive` — disabled reads
    like out-of-range.

## Documentation

- [`.help/docs/using/Timeline.md`](../../.help/docs/using/Timeline.md) — new section on the Global/Local
  display mode and what it means for keyframe editing inside clips; update "Time remapping" to match the new
  behaviour; extend "Working with time clips" to cover non-video texture clips.
- New page under `.help/docs/using/` for the video-editing workflow: build a clip, place it, add a transition.
  [`Plan_VideoClipPlayer.md`](Plan_VideoClipPlayer.md) Phase 4 already lists this as pending — write it once,
  covering both.
- Operator descriptions for the transition op and any renamed ops — authored **in the editor**, since the
  `.md` pages are generated.
- [`.agentic/SOLUTION_OVERVIEW.md`](../SOLUTION_OVERVIEW.md) — the drag-drop / clip section if Phase C changes
  which ops are clip-droppable.

## Manual test sets

- `timeline-clip-time-display.md` — Phase A. Global/Local in identity, stretched, slipped, and nested clips;
  key drag, insert, snap, fence select; mode persistence. Frontmatter: `added: 2026-08-06`,
  `added-in-version: 4.3`.
- `combine-as-time-clip.md` — Phase B. Combine, place, trim, layer, undo; local keyframes travel with the clip.
- `texture-clip-compositing.md` — Phase C. Procedural + video clips on adjacent layers, wired and
  auto-collected; thumbnails on a non-video clip.
- `clip-transitions.md` — Phase D. Cross-fade, wipe, stretch, claiming, export, undo.
- Regression net: `timeline-editing.md`, `dopesheet-curve-expand.md`, `video-clip-player-wired.md`,
  `video-clip-player-autocollect.md`, `video-clip-thumbnails.md`, `timeline-audio-clips.md`.
