# Plan: TimeClip Source Extent + Auto-Collected TimeClips

**Status:** Parts 1+2 implemented (2026-08-09) — Part 1: extent field, ruler editor,
parent-timeline footage, Combine init, ClipRange edit removal, Reset Source to Extent. Part 2:
`[TimeClipPlayer]` op (Lib.flow, AutoCollect of unwired Command time clips, upper rows on top) +
command-colored MagGraph indicator lines. Test set: `.tests-manual/source-extent.md`. Part 3
(local-time view) needs design — see below.

Two related features for combined time-clip operators:

1. **Authored source extent** — a per-Symbol declaration of the content's meaningful time span
   ("this transition/title spans bars 0–8"), edited inside the symbol via dedicated ruler handles,
   and used in the parent timeline the same way video footage duration is used today.
2. **Auto-collected `[TimeClips]` operator** — collects unwired Command time clips of a
   composition, analogous to `AutoCollectClips` on `[AudioBus]` / `[CombineAudio]`.

## Motivation

- The only way to define a clip's source region today is the alt-drag interaction in
  [`ClipRange`](../../Editor/Gui/Windows/TimeLine/ClipRange.cs) *inside* the clip. It mutates
  `SourceRange`/`TimeRange` directly (no command → no undo, no save-relevance tracking), is
  practically undiscovered, and conflates two concepts (see below). The *visualization* of the
  used region is useful; the editing is not.
- Video/audio clips get footage-extent visualization, overrun warnings, and slip editing
  because their content has a known duration
  (`TimeClipItem.TryGetVideoFootageBars` → `MediaClipSourceRegion` →
  [`SourceRegionIndicator`](../../Editor/Gui/Windows/TimeLine/SourceRegionIndicator.cs)).
  Combined command clips have no equivalent — an authored extent supplies it.

## Concept distinction (important)

- **`TimeClip.SourceRange`** (per *instance*, in the parent's `.t3`): which slice of the content
  this particular clip plays. Runtime-relevant; the Player needs it. Unchanged by this plan.
- **Source extent** (per *Symbol*, new): the authored span of meaningful content. Editor-only
  metadata — defaults, visualization, warnings. The Player never reads it.

When the authored extent changes, existing instances' `SourceRange` stays untouched — the parent
timeline simply shows more/less available "footage". No auto-growing of instances.

## Part 1 — Authored source extent

### Data

- New nullable `TimeRange? SourceExtent` (naming TBD; avoid "SourceRange" to prevent confusion)
  on [`TimelineState`](../../Editor/Gui/Windows/TimeLine/TimelineState.cs) — already per-symbol,
  persisted under `Settings.Timeline` in the `.t3ui`. Serialize only when set.
- Fallback when unset: derive from the union of the content's keyframes/clips (what
  `SelectionRangeIndicator.ComputeRange` falls back to) so visualization degrades gracefully.
- `CombineToSymbolDialog` initializes the extent from the combined clips'/keyframes' union, so a
  freshly combined transition is born with a correct extent.

### Editing UI (per approved sketch)

Inside the symbol, the extent renders in the ruler band as a range with slim grab handles at
start and end — same band as the SRI, drawn behind it (like `SourceRegionIndicator` does for
media footage). Interaction grammar copied from `SelectionRangeIndicator`:

- `InvisibleButton` hit zones with `SetNextItemAllowOverlap`; SRI emitted later so it wins
  overlapping hits.
- Start/end handle drag adjusts the extent only — never keyframes, never instances.
- Snapping via `ValueSnapHandler` (extent edges also act as `IValueSnapAttractor` anchors);
  Shift bypasses.
- Undo via a proper command (new small `ChangeTimelineStateCommand` or similar) — store
  composition symbol `Guid`, old/new extent by value; resolve state at `Do`/`Undo` time per the
  command rules in AGENT_INSTRUCTIONS.
- Handle color (decided 2026-08-09): `UiColors.ForegroundFull.Fade(0.8f)`, `1.0` on hover.
  Deliberately neutral — orange `StatusAnimated` would overstate it (nothing is actively
  animated) and blue `StatusAutomated` reads as driven/linked. Shape/placement, not hue,
  distinguishes it from the SRI.

### Consumption in the parent timeline

- Extend the footage lookup in `TimeClipItem` so command clips whose symbol declares an extent
  behave like video clips: footage tooltip, "reads past end" warning, and hover/selection
  publishing through `MediaClipSourceRegion` → `SourceRegionIndicator` slip editing.
  Generalize `TryGetVideoFootageBars` into a `TryGetContentExtent` that checks (a) video
  duration, (b) authored extent. Note the extent's *start* may be non-zero, unlike footage
  which always starts at 0 — the region math needs the start offset.
- New instances created by dragging the op into the timeline default their `SourceRange` (and
  duration) to the extent instead of the 4-bar `DefaultClipDuration`.

### Removal

- Delete the alt-drag manipulation from `ClipRange` (keep the shaded visualization of the
  instance's `SourceRange`). This removes the only un-undoable timeline edit.

## Part 2 — Auto-collected `[TimeClips]` operator

An op (working name `[TimeClips]` / `[CollectTimeClips]`) with an `AutoCollect` flag that
executes unwired Command time clips of its composition, so "drop a clip and it plays" works for
command clips like it does for audio ([`AudioClipCollector`](../../Core/Audio/AudioClipCollector.cs)).

- **Template:** the audio pattern — child scan cached on `Symbol.VersionCounter`, re-scan on
  structure change; one auto-collecting op per composition (document, don't enforce hard).
- **Scope:** collect only clips whose command output has no outgoing connection (mirror how the
  audio scanner decides "loose"); a clip wired anywhere is excluded to avoid double execution.
- **Ordering:** deterministic by `LayerIndex` (matching the timeline's visual stacking order),
  ties by clip start time. This is user-visible for Commands (blending), unlike audio.
- **Graph indicator:** `MagGraphCanvas.AutoCollectIndicators` already draws collector↔clip
  lines; tint per collected type — command color for this op.
- **Cross-check** with `Plan_TimeClipEvaluation.md` (`UsedForRegionMapping` /
  `IPreventingTimeRemap` consolidation) so the collector doesn't bake in assumptions that plan
  refactors away.

## Adjacent cleanup

- `MoveTimeClipsCommand` still stores `Instance` references (flagged as latent bug in
  AGENT_INSTRUCTIONS). The new extent command is in the same family — fix the old one while
  establishing the new pattern.

## Docs & tests

- `.help/`: extend the timeline/clips page with source extent + `[TimeClips]` collection.
- `.tests-manual/`: new test set covering extent editing (create via combine, drag handles,
  undo, snap), parent-timeline footage display for a command clip, and auto-collection
  (drop clip → plays; wire clip → collector releases it). Steps with explicit start states and
  numeric expectations.

## Part 3 — Local-time view inside entered clips (not started; needs design)

Editing inside a combined time-clip symbol is currently dangerous: the playhead shows global time
(e.g. bar 174) while extent-anchored content lives at 0..2, so newly set keyframes silently land
hundreds of bars away from the content. (Discovered 2026-08-09 while testing Part 1 — a "remap
bug" that turned out to be keyframes authored at the wrong time.) For classic `[TimeClip]`-style
ops the current behavior is correct — their content is authored in place (`SourceRange` ≈
placement) — so any fix must distinguish the two cases.

The intended fix is the NLE behavior: **entering a clip instance remaps the timeline into source
time** — view, playhead display, keyframe writes, recording, and snapping all go through the
instance's affine `MapTimelineToSource`/`MapSourceToTimeline`. Notes from the discussion:

- The mapping is affine, so the canvas transform part is cheap; the hard part is every *write*
  path (keyframe creation, recording, drag interactions) and the playhead, which is global
  (`Playback` is shared across compositions — the parent keeps rendering at global time).
- Entry context matters: the remap needs the *instance* the user entered through; entering the
  symbol without a clip context (e.g. from the symbol library) has no mapping to apply.
- Overlaps with `Plan_TimeClipEvaluation` (`UsedForRegionMapping` / `IPreventingTimeRemap`
  consolidation) — the "does this clip remap content time" question must have one answer shared
  by evaluation and the editor view.
- Lighter mitigations (out-of-extent playhead warning; jump-playhead-to-local-time affordance)
  were considered and deliberately skipped in favor of this full solution.

Status: user is thinking about the design; do not start without a discussion.

## Open questions

1. Final name for the extent field/UI label ("Source Extent", "Content Range", …).
2. Should the parent-side slip drag clamp to the authored extent, or allow overrun with the
   warning only (video behavior today: warn, don't clamp)? Leaning: match video — warn only.
3. Does `[TimeClips]` also need a wired (non-auto) mode with explicit command inputs, or is
   auto-collect-only enough for v1?
