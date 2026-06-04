# Video clip player

**Status:** Drafted 2026-06-04. Design agreed; no code yet. Follows the FFmpeg playback work
([`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md)) which made frame-precise video playback cheap enough
to composite several clips on one timeline.

## Goal

A single **`VideoClipPlayer`** operator produces **one continuous `Texture2D`** by compositing the
video clips that are active at the current playback time, stacked **bottom→top by `LayerIndex`**. It
draws from **two sources at once**:

1. **Wired clips** — `VideoClip` ops connected into the player (idiomatic, like [`Group`](../../Operators/Lib/render/transform/Group.cs)).
2. **Auto-collected clips** — sibling `VideoClip` ops in the same composition, discovered by scanning
   (no wires), gated by an `AutoCollect` input (the "Auto play" idea), like
   [`GetAllSpatialAudioPlayers`](../../Operators/Lib/io/audio/_/GetAllSpatialAudioPlayers.cs).

This removes today's friction: every `[PlayVideoClip]` currently owns its own output `Texture2D`, so
stitching several cut clips into one continuous output means a manual `[Blend]`/`[Layer]` chain, and
off-screen clips keep decoding. The player owns the composite and only the active clips decode.

`[PlayVideo]` (the single-file, graph-driven op) is **out of scope** and unchanged — it covers the
"one video, programmatically scrubbed" case the way `[AudioPlayer]` covers graph-driven audio.

## Why this is mostly assembly, not new infrastructure

Four existing mechanisms cover most of it:

- **Timeline discovery is automatic.** Any op whose output is a `TimeClipSlot` shows on the timeline —
  the editor scans children for `ITimeClipProvider` ([`Structure.GetAllTimeClips`](../../Editor/UiModel/ProjectHandling/Structure.cs#L140)).
  `VideoClip` keeps its `TimeClipSlot`, so clips appear and stack on layers with no extra work.
- **Active-set + source-time remap is free.** [`TimeClipSlot.UpdateWithTimeRangeCheck`](../../Core/Operator/Slots/TimeClipSlot.cs#L53)
  already no-ops a clip outside its `TimeRange` and remaps `LocalTime`/`LocalFxTime` from
  `TimeRange`→`SourceRange` for active clips. The manual remap in
  [`PlayVideoClip.cs:33-51`](../../Operators/Lib/io/video/PlayVideoClip.cs) becomes redundant.
- **Sibling scan has a precedent.** `GetAllSpatialAudioPlayers` walks `Parent.Children` for a marker
  interface — exactly the no-wires collection the `AutoCollect` path needs.
- **Composite-into-a-target has a precedent.** [`RenderTarget`](../../Operators/Lib/image/generate/basic/RenderTarget.cs)
  binds/clears a target and evaluates a sub-tree into it with full save/restore of context state;
  [`DrawScreenQuad`](../../Operators/Lib/render/basic/DrawScreenQuad.cs) blits a `Texture2D` into the
  bound target with a `BlendMode` and `Color`. The player reuses both.

The one genuinely new thing is the player **driving** the scanned clips' evaluation (Phase 2). The
wired path (Phase 1) is just `Group` specialised to video, so it carries no new risk.

## Architectural decisions (proposed — confirm before Phase 2)

- **One player, one output.** `VideoClipPlayer` owns an internal render target and outputs its
  `Texture2D`. Single op, single output.
- **Two sources, one composite.** Wired (`MultiInput`) and scanned (`AutoCollect`) clips are merged
  into one list, **deduped by `SymbolChildId`** (a clip can be both wired and auto-collected),
  filtered to the active set (`TimeRange` contains `LocalTime`), and **sorted by `LayerIndex` only** —
  a single ordering rule across both sources. The timeline is the source of truth for stacking.
- **Player composites; clips stay simple (recommended).** `VideoClip` keeps producing a `Texture2D`
  and exposes `Opacity` / `BlendMode` / its `TimeClip` via an `IVideoClipProvider` interface. The
  player runs one `DrawScreenQuad`-style blend per active clip into its render target, reading params
  off the interface (so opacity/blend work identically for wired and scanned clips — they don't have
  to travel down a texture wire). *Alternative considered:* make each `VideoClip` a self-drawing
  `Command` (DrawScreenQuad internally) and let the player just bind the target and drive commands in
  order (pure `Group`). Simpler player, but the wired/scanned paths diverge and the player can't do
  cross-clip effects later. The Phase-1 prototype settles this; the plan assumes player-composites.
- **No new `EvaluationContext` instance.** The "separate context" is a save→mutate→evaluate→restore
  scope on the *current* context, as `RenderTarget`/`Group` already do. Let each clip's own
  `TimeClipSlot` perform the gating + source-time remap rather than hand-rolling it in the player —
  this avoids the known sharp edge noted at [`TimeClipSlot.cs:61`](../../Core/Operator/Slots/TimeClipSlot.cs#L61)
  ("setting local time should flag time accessors as dirty").
- **Driving is isolated to the scanned set.** Wired clips flow through normal `MultiInput`
  invalidation (zero new risk). Only auto-collected clips are force-evaluated by the player. Build
  order follows from this: wired first, scan behind the flag.
- **Per-clip `VideoPlaybackController`.** Each `VideoClip` keeps its own decoder + intermediate
  texture (as today). A shared decode pool in the player is deferred — crossfades need ≥2 live
  decoders anyway, so "one texture total" is not a real target.
- **Composition-scoped scan.** `AutoCollect` walks the player's own composition only (no nesting),
  matching the audio precedent.
- **Rename keeps the Guid.** `PlayVideoClip` → `VideoClip` preserves symbol Guid
  `04c1a6dc-3042-48a8-81d2-0a5a162016dc` so existing projects load. New inputs are additive.
- **A lone `VideoClip` with no player renders nothing.** Intended (like a `TimelineAudioClip` without
  the audio engine), but surface it as an operator status hint so it isn't mistaken for a bug.

## Interface sketch

```csharp
// Implemented by VideoClip; consumed by VideoClipPlayer for both wired and scanned clips.
internal interface IVideoClipProvider
{
    TimeClip TimeClip { get; }          // TimeRange (active test) + LayerIndex (ordering)
    Slot<Texture2D> VideoTexture { get; } // player pulls this to make the clip decode its current frame
    float Opacity { get; }
    int   BlendMode { get; }            // SharedEnums.BlendModes, as DrawScreenQuad
}
```

`TimeClipSlot` already implements `ITimeClipProvider`, so for a wired clip the player can reach the
`TimeClip` from the collected slot's `Parent`; the marker interface adds the params and the
texture-pull entry point and lets the scan identify "this child is a video clip."

## Player update loop (target shape)

```
Update(context):
  bind + clear internal RT                              // RenderTarget.cs:99-135 pattern
  candidates = []
  candidates += map(Clips.GetCollectedTypedInputs() -> IVideoClipProvider)   // wired
  if AutoCollect:
      candidates += Parent.Children where child is IVideoClipProvider        // scanned
  dedup by SymbolChildId
  active = candidates where TimeClip.TimeRange contains context.LocalTime
  sort active by TimeClip.LayerIndex                    // bottom -> top, single rule
  for clip in active:
      tex = clip.VideoTexture.GetValue(context)         // drives decode (wired: idiomatic; scanned: forced)
      screenQuadBlend(tex, clip.Opacity, clip.BlendMode) into RT   // DrawScreenQuad shader path
      if context.Playback.IsRenderingToFile:
          Playback.OpNotReady |= !clipIsReady           // keep export frame-exact (see PlayVideo.cs:51)
  restore RT + context
  Output.Value = RT texture
```

## Current state — what exists

- [`Operators/Lib/io/video/PlayVideoClip.cs`](../../Operators/Lib/io/video/PlayVideoClip.cs) (+ `.t3`/`.t3ui`) —
  rename target. Outputs `Texture2D` + `TimeClipSlot<Command>`; owns a `VideoPlaybackController`.
- [`Video/VideoPlaybackController.cs`](../../Video/VideoPlaybackController.cs) — per-clip decode worker
  + texture upload + export-gated `IsReady`. Reused unchanged.
- [`Core/Operator/Slots/TimeClipSlot.cs`](../../Core/Operator/Slots/TimeClipSlot.cs) — gating + remap.
- [`Operators/Lib/render/transform/Group.cs`](../../Operators/Lib/render/transform/Group.cs) — wired
  collect + draw + opacity-via-`ForegroundColor` template (Phase 1).
- [`Operators/Lib/flow/Switch.cs`](../../Operators/Lib/flow/Switch.cs) —
  `LimitMultiInputInvalidationToIndices` (Phase 3 efficiency for the wired set).
- [`Operators/Lib/image/generate/basic/RenderTarget.cs`](../../Operators/Lib/image/generate/basic/RenderTarget.cs) —
  RT bind/clear/save-restore.
- [`Operators/Lib/render/basic/DrawScreenQuad.cs`](../../Operators/Lib/render/basic/DrawScreenQuad.cs) —
  texture→bound-RT blit with `BlendMode`/`Color`.
- [`Operators/Lib/io/audio/_/GetAllSpatialAudioPlayers.cs`](../../Operators/Lib/io/audio/_/GetAllSpatialAudioPlayers.cs) —
  sibling-scan-by-marker-interface template (Phase 2).
- [`Operators/Lib/io/video/PlayVideo.cs:51`](../../Operators/Lib/io/video/PlayVideo.cs#L51) — the
  export `OpNotReady` pattern to replicate per active clip.

## Phases

### Phase 1 — Rename + clip params + player in wired mode

**Goal:** `VideoClipPlayer` composites *wired* `VideoClip`s into one render target. Fully idiomatic;
no scanning, no force-evaluation. Ship-able on its own.

**Scope:**

- Rename `PlayVideoClip` → `VideoClip`: class + `.cs`/`.t3`/`.t3ui` filenames; **keep Guid**
  `04c1a6dc-…`. Update the `/*PlayVideoClip*/` name comments (regenerated on save). Drop the now-dead
  manual remap (lines 33-51) and lean on `TimeClipSlot`.
- Add `VideoClip` inputs: `Opacity` (float, default 1), `BlendMode` (int, `SharedEnums.BlendModes`).
  Additive — no migration.
- Add `IVideoClipProvider` (interface above) and implement it on `VideoClip`.
- New `VideoClipPlayer` op: `MultiInputSlot<Texture2D> Clips`, `bool AutoCollect` (default **off** in
  this phase), `Slot<Texture2D> Output`. Update loop above with the *wired* branch only; composite via
  the `DrawScreenQuad` shader path into an internal RT (model the RT lifecycle on `RenderTarget`).
- Resolution: default to `context.RequestedResolution`; optional `Resolution` input later.

**Testable outcome:** Place two `VideoClip`s at overlapping/adjacent `TimeRange`s, wire both into a
`VideoClipPlayer`. Output shows the active clip(s); at an overlap both composite with their `Opacity`/
`BlendMode`; stacking follows `LayerIndex`. Scrubbing/export render the right frames. A single wired
clip behaves like today minus the manual remap.

**Effort:** ~1–1.5 days. The real work is the player's RT + per-clip blend; the rename is mechanical.

### Phase 2 — `AutoCollect` (scan + drive)

**Goal:** with `AutoCollect` on, the player also composites unwired sibling `VideoClip`s.

**Scope:**

- Scan `Parent.Children` for `IVideoClipProvider` (guard `Parent == null`, log once like
  `GetAllSpatialAudioPlayers`).
- Merge with the wired list; dedup by `SymbolChildId`; same active-filter + `LayerIndex` sort.
- **Drive** each scanned clip's `VideoTexture.GetValue(context)` inside the save/restore scope. This
  is the new bit — validate that forced evaluation re-decodes correctly per frame and doesn't
  double-evaluate a clip that is also reachable elsewhere.
- Status hint on a `VideoClip` that no player is currently drawing.

**Testable outcome:** Drop several `VideoClip`s in a composition (no wires) + one `VideoClipPlayer`
with `AutoCollect` on → continuous playback across cuts; toggling `AutoCollect` off falls back to
wired-only; a clip both wired and scanned draws once.

**Effort:** ~1–2 days, mostly de-risking the forced evaluation. If it fights the dirty-flag system,
Phase 1 (wired) is unaffected.

### Phase 3 — Efficiency, lifecycle, export hardening

**Goal:** off-screen clips cost nothing; export is frame-exact for every active clip.

**Scope:**

- Wired set: feed active indices into `Clips.LimitMultiInputInvalidationToIndices` (Switch pattern) so
  inactive branches don't invalidate/decode.
- Scanned set: skip inactive clips before pulling them (already implied by the active-filter); consider
  releasing a `VideoPlaybackController`'s decode session after N idle frames (audio-style stale
  thresholding) to cap memory on many-clip projects.
- Thread `Playback.OpNotReady |= !IsReady` per active clip so export waits for every contributing clip.
- Decide per-clip-controller vs shared pool based on observed memory; default: keep per-clip.

**Testable outcome:** A 20-clip timeline with 1–2 active at a time holds steady decode/memory; export
of a crossfade is frame-exact on both clips.

**Effort:** ~1 day.

### Phase 4 — Naming, docs, tests, deprecation

**Scope:**

- Finalise input naming (`AutoCollect` vs `IncludeTimelineClips` — "Auto play" reads like a transport
  toggle; pick the clearer one before the op ships and locks its input set).
- `.help/` page for the timeline-video-clips workflow; cross-link from the video docs.
- Manual test set (below) under `.tests-manual/`.
- Decide the fate of any example projects using `[PlayVideoClip]` — they keep working via the retained
  Guid; update bundled examples to the new name on next save.

**Effort:** ~0.5–1 day.

## Open questions / deferred

1. **Audio.** Video clips usually carry audio; it's currently muted (BASS routing is backlog, see
   `PlayVideoClip` `Volume` note). This is the same "timeline of clips" problem the audio system
   already solved ([`TimelineAudioClip`](../../Core/Audio/TimelineAudioClip.cs) +
   [`AudioEngine`](../../Core/Audio/AudioEngine.cs) per-clip streams + stale detection). When audio is
   wired up, coordinate per-clip video audio through `AudioEngine` rather than building a parallel
   system — deliberate overlap, not duplication.
2. **Multiple players with `AutoCollect` in one composition** would each draw all clips. Document, or
   scope a player to a layer range, or treat 2nd+ as an error hint.
3. **Forced-evaluation correctness** (Phase 2): the dirty-flag/eval-order implications of a player
   driving siblings — the item to prototype before committing to `AutoCollect`.
4. **Compositing choice** (player-composites vs clip-self-draws `Command`): assumed player-composites;
   confirm with the Phase-1 prototype.
5. **Crossfades** need ≥2 live decoders — fine with per-clip controllers; only a concern if a shared
   pool is introduced.
6. **Resolution / aspect handling** when clips differ in size (fit/cover/stretch, like `[Blend]`'s
   `ScaleMode`). Phase 1 can stretch; expose a mode later.

## Manual test sets (Phase 4)

- `video-clip-player-wired.md` — two wired clips, overlap composite, layer order, scrub, export.
- `video-clip-player-autocollect.md` — unwired clips + `AutoCollect`, continuous cuts, toggle off,
  wired+scanned dedup.
- `video-clip-player-efficiency.md` — many clips, few active; decode/memory steady; crossfade export
  frame-exact.
