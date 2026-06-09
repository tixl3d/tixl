# Audio clip player

**Status:** Phases 1, 2, 3 landed (2026-06-09); building clean. `[AudioClip]` op + AutoPlay registrar + waveform
(Phase 1, user-verified), old `TimelineAudioClip` layer system **deleted** + recording creates `[AudioClip]` ops
(Phase 3), and `[AudioClipPlayer]` (Phase 2) — a single C# op (wired multi-input + AutoCollect) that registers
active clips through the graph, so it drives nested compositions and **exported** playback (the Player calls
`AudioEngine.CompleteFrame`). Also: timeline drop is now generic via `AssetType.TimelineClipOperator`
(Audio→AudioClip, Video→VideoClip, Data→LoadDataClip), with graph-drop = `[Play*]` / timeline-drop = `*Clip`.
Remaining: the Phase 4 soundtrack migration, and per-type drop-duration probes (only audio probed today).
See *Status* under each phase below.
Re-implements timeline **layer** audio clips as `TimeClip`-backed
operators, mirroring [`VideoClip`/`VideoClipPlayer`](Plan_VideoClipPlayer.md). Replaces the bespoke
`TimelineAudioClip` layer-clip system (data in `CompositionSettings.Playback.AudioClips`, hand-rolled
selection / drag / trim / snap / delete / split / inspector) so audio clips become "just another op-clip" and
inherit the entire shared interaction model for free.

**Decided with the user (2026-06-08):**
- **Scope: layer clips only.** The **main soundtrack** (background-waveform draw + FFT for audio-reactive ops +
  export wiring, all via `CompositionSettings.Current`) stays exactly as-is for now. Only the non-soundtrack
  timeline clips — overdubs, recordings, SFX — migrate to ops. Main-soundtrack clips are *already* skipped by
  every layer-clip code path (`if (ac.IsMainSoundtrack) continue;`), so this is a clean cut.
- **Breaking change, no migration.** Alpha; nobody ships audio clips yet. Existing **layer** `TimelineAudioClip`
  entries in saved `CompositionSettings` simply stop appearing. Main-soundtrack entries keep loading. This window
  closes in ~1–2 months once real projects use the feature — hence doing it now.
- **`AutoPlay` (clip) is the reverse of `AutoCollect` (player).** A clip with `AutoPlay` registers itself for
  playback with no player op required (the everyday "drop a clip and hear it" path, preserving today's UX). A
  clip without it relies on an `[AudioClipPlayer]` to collect + drive it. Default `AutoPlay = true`.
- **Future soundtrack migration (deferred, Phase 4+).** Once `DisplayAs` lands, a migration can convert each
  main soundtrack into an `[AudioClip]` op with `AutoPlay = true` + `DisplayAs = BackgroundImage`, folding the
  soundtrack into the same op model and finally retiring `CompositionSettings.Playback.AudioClips`.

## Goal

Timeline audio clips are **operators**: an `[AudioClip]` op carries a `TimeClipSlot` (placement) plus
per-clip inputs (`Path`, `Volume`, `Mute`, `AutoPlay`, `DisplayAs`), shows its **waveform** in the clip body,
and is **registered with `AudioEngine`** for scheduled BASS playback when active. Two registration paths,
mirroring video:

1. **`AutoPlay` (clip self-registers)** — a per-frame composition scan finds `[AudioClip]`s with `AutoPlay` on
   and registers the active ones. Graph-independent (an unwired clip still plays), exactly how today's
   `TimelineAudioClip`s play via `PlaybackUtils.UpdatePlaybackAndSyncing`.
2. **`[AudioClipPlayer]` + `AutoCollect` (player drives)** — an explicit player op collects clips (wired
   multi-input or scanned siblings) and registers them during its own `Update`. For grouping / explicit
   control / clips with `AutoPlay = false`. Mirrors `[VideoClipPlayer]`.

`[AudioPlayer]` (the existing single-file, graph-driven op,
[`Operators/Lib/io/audio/AudioPlayer.cs`](../../Operators/Lib/io/audio/AudioPlayer.cs)) is **out of scope and
unchanged** — it covers "one audio file, programmatically scrubbed," the audio analog of `[PlayVideo]`.

## Why this is mostly deletion, not new infrastructure

The current layer-clip system exists **only because `TimelineAudioClip` is not a `TimeClip`**. Make it one and
the following all become dead code — every one of them re-implements something the op-clip path already does:

| Deleted / gutted | Replaced by |
| --- | --- |
| `Editor/.../TimeClips/AudioClipInteractions.cs` (selection set, file-drop, delete, snap, cross-drag) | Shared `TimeClipInteractions` |
| `Editor/.../TimeClips/TimelineAudioClipItem.cs` (rendering + bespoke drag/trim/ratchet) | `TimeClipItem` + a new `AudioClipBodyRenderer` |
| `Editor/.../TimelineAudioClipInspector.cs` | The normal parameter window (op inputs) |
| `AddTimelineAudioClipCommand` / `DeleteTimelineAudioClipsCommand` / `MoveTimelineAudioClipsCommand` | `AddSymbolChildCommand` / `DeleteSymbolChildrenCommand` / `MoveTimeClipsCommand` |
| The audio branches in `ClipArea.cs` (`AudioClips.*`, the `CompositionEnabled` dimming, the layer-bounds merge) | Op-clips draw through the standard layer area |
| `CompositionSettings.Playback.AudioClips` as layer storage (kept only for the main soundtrack until Phase 4) | `[AudioClip]` op instances on the timeline |
| The layer-clip half of the `PlaybackUtils` registration loop | The `AutoPlay` registrar + `[AudioClipPlayer]` |

Three existing mechanisms make the new path cheap:

- **Timeline discovery is automatic.** Any op whose output is a `TimeClipSlot` shows on the timeline —
  `Structure.GetAllTimeClips` scans children for `ITimeClipProvider`. `[AudioClip]` appears + stacks on layers
  with no editor work, and gets drag / trim / split (`TimeClipInteractions.SplitClipsAtTime`) / snap /
  multi-select / delete for free.
- **Body content has a precedent.** `DataClipBodyRenderer.TryDraw` (called from `TimeClipItem`) already paints
  per-channel overlays into a `DataClip` op-clip body; `AudioClipBodyRenderer` mirrors it, reusing the existing
  waveform cache (`Editor/Gui/Audio/AudioImageGenerator.cs` + the `TryGetWaveformSrv` path that
  `TimelineAudioClipItem` uses today). `[AudioClip]` implements the `IContentTimeClip` marker
  ([`TimeClipSlot.cs:41`](../../Core/Operator/Slots/TimeClipSlot.cs)) — same as `VideoClip` — to signal "renders
  its own body."
- **Sibling scan has a precedent.** `GetAllSpatialAudioPlayers` and `_ProcessVideoClips` both walk
  `Parent.Children` for a marker interface — exactly the `AutoCollect` / `AutoPlay` scan.

## The one real difference from video: scheduled, not pulled

Video is **pulled** — a texture per frame, composited by the player into a render target. Audio is
**scheduled** — BASS plays it continuously on its own thread; the per-frame job is only to tell the engine
"this clip, at this source offset, is active now" via `AudioEngine.UseSoundtrackClip(handle, time)`
([`AudioEngine.cs:149`](../../Core/Audio/AudioEngine.cs)), with `AudioEngine.CompleteFrame` advancing it. So:

- Neither the registrar nor `[AudioClipPlayer]` *composites* anything — they **register** active clips. There's
  no render target, no `DrawScreenQuad`, no `RenderTargetReference` indirection. Much simpler than the video helper.
- **The op owns no stream lifecycle — don't build start/stop/seek.** `UseSoundtrackClip(handle, time)` is a
  per-frame *heartbeat*: call it every frame for each clip whose `TimeRange` contains the playhead, and that's it.
  The engine creates the BASS stream on first call, does all pause/seek/volume/resync in
  `SoundtrackClipStream.UpdateSoundtrackTime`, and **frees the stream automatically the first frame you stop
  calling** (`ProcessSoundtrackClips`' `IsInUse` stale-eviction, `AudioEngine.cs:242-251,283-287`). So "stop a
  clip" = "stop registering it" — the registrar/player must never `Bass.StreamFree`, seek, or pause directly.
  This also means live-stream count ≈ *active* clips (not total), so the eviction half of a pool already exists;
  a bounded preroll/eviction pool (cf. `VideoPlaybackEngine`) is **not needed for v1** — deferred as cut-in-gap
  polish (preroll = pre-register a clip a few ms before its start so its stream is warm at the cut).
- **Unwired op-clips don't `Update`.** (Confirmed by `DataClipBodyRenderer`'s file-fallback path, which exists
  precisely because an unwired clip's slot value is null.) So `AutoPlay` **cannot** rely on the clip's own
  `Update` — it needs the **per-frame composition scan** (the registrar), which is what `PlaybackUtils` already
  does for `CompositionSettings` clips today. We point that scan at `IAudioClipProvider` op-children instead.
- `[AudioClipPlayer]` **does** require itself to be in the evaluated graph (like `[VideoClipPlayer]` /
  `[SimulateIoData]`) — its `Update` is where it registers its collected clips. That's the deliberate trade for
  the "explicit control" path; the `AutoPlay` registrar covers the graph-independent "always plays" case.

## Interface sketch

```csharp
// Implemented by [AudioClip]; consumed by the AutoPlay registrar and [AudioClipPlayer].
internal interface IAudioClipProvider
{
    TimeClipSlot<Command> TimeSlot { get; }   // TimeRange (active test) + LayerIndex; placement
    AudioClipResourceHandle ResolveHandle();   // file -> BASS stream handle (cached)
    InputSlot<float> VolumeInput { get; }
    InputSlot<bool>  MuteInput { get; }
    bool AutoPlay { get; }                      // self-register via the global scan
    void MarkManaged();                         // a player stamps clips it drives (status hint), cf. VideoClip
}
```

`[AudioClip]` : `Instance<AudioClip>, IStatusProvider, IAudioClipProvider, IContentTimeClip`
- Outputs: `TimeClipSlot<Command> TimeSlot` (placement + `Command` passthrough). No texture/audio "value" output —
  audio is registered, not pulled.
- Inputs: `Path` (string), `Volume` (float, 1), `Mute` (bool), `AutoPlay` (bool, **default true**),
  `DisplayAs` (enum `LayerClip` | `BackgroundImage`, default `LayerClip` — `BackgroundImage` reserved for the
  Phase-4 soundtrack migration, not consumed yet), and the source trim (`SourceOffset` / duration via the
  `TimeClip.SourceRange`, as video does).
- `Update`: resolve the `AudioClipResourceHandle`, set status. Minimal — playback registration happens in the
  registrar / player.
- Status hint when no registrar/player has it (like `VideoClip`: "Not played — enable AutoPlay or add an
  [AudioClipPlayer]").

`[AudioClipPlayer]` : `MultiInputSlot AudioClips`, `bool AutoCollect`, `Slot<Command> Output` (passthrough so it
sits in a `Command` chain like `[SimulateIoData]`). `Update`: collect wired + (if `AutoCollect`) scanned
siblings, dedup by `SymbolChildId`, register the active set with `AudioEngine`.

## Registrar / player update shape

```
// AutoPlay registrar — runs each frame from the editor playback update (and the Player),
// extending today's PlaybackUtils.UpdatePlaybackAndSyncing soundtrack loop.
RegisterAutoPlayClips(composition, time):
  for child in composition.Children where child is IAudioClipProvider and child.AutoPlay:
      clip = child.TimeSlot.TimeClip
      if clip.TimeRange contains localTime and not Mute:
          AudioEngine.UseSoundtrackClip(child.ResolveHandle(), time)   // with Volume
          child.MarkManaged()

// [AudioClipPlayer].Update — explicit path for AutoPlay=false clips / grouping
Update(context):
  candidates  = map(AudioClips.CollectedInputs -> IAudioClipProvider)      // wired
  if AutoCollect: candidates += Parent.Children where IAudioClipProvider   // scanned (skip AutoPlay ones?)
  dedup by SymbolChildId
  for clip in candidates where active(clip, localTime):
      AudioEngine.UseSoundtrackClip(clip.ResolveHandle(), time); clip.MarkManaged()
  Output.Value = passthrough
```

(Open question below: whether the player should *skip* `AutoPlay` clips to avoid double-registration, or whether
`AudioEngine`'s handle-keyed `SoundtrackClipStreams` dict already dedups a clip registered twice in a frame.)

## Current state — what exists to reuse / delete

- [`Core/Audio/AudioEngine.cs`](../../Core/Audio/AudioEngine.cs) — `UseSoundtrackClip` (149) / `CompleteFrame`
  (174); handle-keyed `SoundtrackClipStreams`. Registration target, **unchanged**.
- [`Core/Audio/TimelineAudioClip.cs`](../../Core/Audio/TimelineAudioClip.cs) — the data model + `AudioClipResourceHandle`.
  `AudioClipResourceHandle` is **reused** by `[AudioClip]`; the `TimelineAudioClip` POCO stays only for the main
  soundtrack until Phase 4.
- [`Editor/Gui/Interaction/Timing/PlaybackUtils.cs:25-37`](../../Editor/Gui/Interaction/Timing/PlaybackUtils.cs) —
  the loop that registers `CompositionSettings.Playback.AudioClips`. Soundtrack half stays; layer half → registrar.
- [`Editor/Gui/Windows/TimeLine/TimeClips/DataClipBodyRenderer.cs`](../../Editor/Gui/Windows/TimeLine/TimeClips/DataClipBodyRenderer.cs)
  — body-overlay template for `AudioClipBodyRenderer`.
- [`Editor/Gui/Audio/AudioImageGenerator.cs`](../../Editor/Gui/Audio/AudioImageGenerator.cs) + the
  `TryGetWaveformSrv` path in `TimelineAudioClipItem` — waveform image + cache, reused by the body renderer.
- [`Operators/Lib/io/video/VideoClip.cs`](../../Operators/Lib/io/video/VideoClip.cs) /
  [`_ProcessVideoClips.cs`](../../Operators/Lib/io/video/_ProcessVideoClips.cs) — op + provider + AutoCollect template.
- [`Operators/Lib/io/audio/_/GetAllSpatialAudioPlayers.cs`](../../Operators/Lib/io/audio/_/GetAllSpatialAudioPlayers.cs) —
  sibling-scan-by-marker template.
- [`Editor/Gui/Windows/TimeLine/RecordingSession.cs`](../../Editor/Gui/Windows/TimeLine/RecordingSession.cs) —
  already creates a `LoadDataClip` **op** for data; Phase 3 makes it create an `[AudioClip]` op for audio (symmetry).
- The **delete list** in *Why this is mostly deletion* above.

## Phases

### Phase 1 — `[AudioClip]` op + waveform body + AutoPlay (vertical slice)

**Goal:** drop an `[AudioClip]` on the timeline → it appears as a normal op-clip, shows its waveform, plays via
`AutoPlay`, and supports drag / trim / **split** / snap / multi-select / delete with zero new interaction code.
The bespoke `TimelineAudioClip` layer system is removed in the same phase (no parallel systems left alive).

**Scope:**
- New `[AudioClip]` op (new Guid): `TimeClipSlot<Command>`, `IAudioClipProvider`, `IContentTimeClip`,
  `IStatusProvider`; inputs `Path` / `Volume` / `Mute` / `AutoPlay` (default true) / `DisplayAs` (enum,
  `LayerClip` only consumed). Resolve `AudioClipResourceHandle`.
- `AudioClipBodyRenderer` (mirror `DataClipBodyRenderer`), hooked into `TimeClipItem`; waveform via the existing
  generator/cache. Honor `Mute` (faded) — the one bit worth keeping from `TimelineAudioClipItem`.
- The **AutoPlay registrar**: extend `PlaybackUtils.UpdatePlaybackAndSyncing` (and the Player's equivalent) to
  scan the active composition for `IAudioClipProvider` + `AutoPlay` and register active, non-muted clips.
- **Delete** `AudioClipInteractions`, `TimelineAudioClipItem`, `TimelineAudioClipInspector`, the three
  `*TimelineAudioClipsCommand`s, the audio branches in `ClipArea`, and the layer half of the `PlaybackUtils` loop.
  Keep `TimelineAudioClip` + the soundtrack path.

**Testable outcome:** an `[AudioClip]` plays when the playhead is inside its range; drag/trim/split/snap/delete
work via the shared path; waveform draws; `Mute` silences + fades; removing the op stops playback.

**Effort:** ~2–3 days. Most of it is the registrar + body renderer + the careful deletion sweep.

**Status: IN PROGRESS (2026-06-08) — new system built additively; builds clean (Core + Lib + Editor). NOT yet
runtime-verified, and the old system is NOT yet deleted (deliberate — see below).**
- **New `[AudioClip]` op** ([`Operators/Lib/io/audio/AudioClip.cs`](../../Operators/Lib/io/audio/AudioClip.cs) +
  `.t3`/`.t3ui`, Guid `f0008b50-091d-4e9f-91eb-baa212acfa20`): `TimeClipSlot<Command>` output;
  `IAudioClipProvider` + `IContentTimeClip` + `IStatusProvider` + `IDescriptiveFilename`; inputs
  `Command`/`Path`/`Volume`/`Mute`/`AutoPlay`(default true). Holds a `TimelineAudioClip` synced from inputs.
  **`DisplayAs` was deliberately dropped** from v1 — per the VideoClipPlayer lesson "don't ship a dead input
  before its consumer exists"; it's additive and lands with the Phase-4 soundtrack migration.
- **Core contract** (justified Core additions, mirroring `ITimeClipProvider`):
  [`Core/Audio/IAudioClipProvider.cs`](../../Core/Audio/IAudioClipProvider.cs) +
  [`Core/Audio/AudioClipCollector.cs`](../../Core/Audio/AudioClipCollector.cs) (the AutoPlay registrar — scans
  `composition.Children` for `IAudioClipProvider`+AutoPlay, registers active ones via `UseSoundtrackClip`).
- **Registrar wired** into `PlaybackUtils.UpdatePlaybackAndSyncing` (runs every frame, unconditional of
  AudioSource, coexists with the `CompositionSettings` loop).
- **Waveform body** ([`AudioClipBodyRenderer.cs`](../../Editor/Gui/Windows/TimeLine/TimeClips/AudioClipBodyRenderer.cs))
  hooked into `TimeClipItem` next to `DataClipBodyRenderer`; reuses `AudioImageFactory` + the SRV cache. v1 is a
  full-image stretch (no source-window UV crop) and no mute-fade yet.
- **Deletion sweep — DONE (2026-06-09).** Removed `AudioClipInteractions`, `TimelineAudioClipItem`,
  `TimelineAudioClipInspector`, and the `Add`/`Delete`/`Move` `TimelineAudioClipsCommand`s; stripped the audio
  branches from `ClipArea`, the cross-drag hooks from `TimeClipInteractions`, and the inspector route from
  `ParameterWindow`. **Phase 3 folded in:** `RecordingSession` now creates an `[AudioClip]` op for the audio take
  (mirrors the `LoadDataClip` op — grows its TimeClip during capture, commits the Path input on stop via
  `ChangeInputValueCommand`). `TimelineAudioClip` (the data class) and `CompositionSettings.Playback.AudioClips`
  **stay** — still used by the main soundtrack, the `[AudioClip]` op's internal carrier, the engine, and
  `[PlayAudioClip]`. One UX drop: timeline-clip-area audio file-drop is gone (it lived in `AudioClipInteractions`);
  graph/library drop creates an `[AudioClip]` op via the AssetType mapping instead.
- **v1 simplifications to revisit:** source-trim mapping is `SourceOffsetSecs = SecondsFromBars(SourceRange.Start)`
  + full-stretch waveform (Plan open question #3); the registrar reads **static** `TypedInputValue` for unwired
  clips, so animated `Volume`/`Mute`/`Path` on an unwired clip won't update (acceptable for v1).

### Phase 2 — `[AudioClipPlayer]` + `AutoCollect`

**Goal:** explicit-control path for `AutoPlay = false` clips / grouping.

**Scope:** new `[AudioClipPlayer]` op (`MultiInput AudioClips`, `AutoCollect`, `Command` passthrough). `Update`
collects wired + scanned siblings, dedups by `SymbolChildId`, registers the active set. `MarkManaged` status
hint on clips no registrar/player drives. Resolve the double-registration question (skip-AutoPlay vs.
engine-dedups).

**Testable outcome:** clips with `AutoPlay = false` are silent until an `[AudioClipPlayer]` (wired or
`AutoCollect`) drives them; a clip both wired and scanned plays once.

**Effort:** ~1 day.

**Status: DONE (2026-06-09) — as built:**
- Single C# op ([`Operators/Lib/io/audio/AudioClipPlayer.cs`](../../Operators/Lib/io/audio/AudioClipPlayer.cs) +
  `.t3`/`.t3ui`, Guid `3f78a8a8-…`) — *not* a symbol graph like `[VideoClipPlayer]`, because audio registers
  rather than composites. Inputs: `MultiInputSlot<Command> AudioClips`, `bool AutoCollect` (default true);
  `Slot<Command> Output` (Animated) so it sits in a render command chain like `[SimulateIoData]`.
- `Update` uses `context.LocalTime` (bars) + `context.Playback.SecondsFromBars` (secs) so nested compositions
  get locally-correct timing. Collects wired clips via each multi-input slot's `Parent`, then (if AutoCollect)
  scans `Parent.Children` for `IAudioClipProvider`, deduped by `SymbolChildId` in a reused `HashSet`.
- Per-clip registration factored into Core `AudioClipCollector.RegisterIfActive` (shared with the AutoPlay
  registrar). The player `MarkManaged`s every clip it drives (active or not) for the status hint, then registers
  the active ones.
- **Double-registration resolved:** the engine keys `_updatedSoundtrackClipTimes` by handle and the op returns
  one cached handle, so a clip registered by both registrar and player collapses to one entry — no skip needed.
- **`AudioClip` status is now AutoPlay-aware:** AutoPlay clips read Success (self-playing); only `AutoPlay=false`
  clips with no recent player show the "not played" Notice.
- **Export:** the player is evaluated in the Player's render path and the Player calls `AudioEngine.CompleteFrame`
  each frame (`Program.RenderLoop.cs:51`), so placing an `[AudioClipPlayer]` makes the new clips sound in export.
  The editor-only AutoPlay registrar still doesn't run in the Player — so export needs the player op (or a future
  registrar-in-Player wiring, deferred).

### Phase 3 — Recording integration (symmetry with data)

**Goal:** `RecordingSession` creates an `[AudioClip]` op for the audio take, exactly as it creates a
`LoadDataClip` op for the data take — both placed via `GraphUtils.FindFreePosition`, both undone by the session
`MacroCommand`.

**Scope:** replace the `TimelineAudioClip` + `AddTimelineAudioClipCommand` path in `RecordingSession` with an
`AddSymbolChildCommand` for `[AudioClip]` (set `Path` on stop, grow the `TimeClip.TimeRange` live during
capture). Naming already lands as `AudioRec-NNN` (done).

**Testable outcome:** record audio → an `[AudioClip]` op appears on a fresh layer, grows during capture, plays
back after stop; single Ctrl+Z removes the whole session.

**Effort:** ~1 day.

### Phase 4 — Soundtrack migration + docs/tests (deferred)

**Goal:** retire `CompositionSettings.Playback.AudioClips` entirely.

**Scope (deferred — confirm before starting):** implement `DisplayAs = BackgroundImage` (draw the clip as the
timeline background waveform, the current main-soundtrack visual) and route FFT-for-audio-reactive + export to
find the `BackgroundImage` `[AudioClip]` through the op graph instead of `CompositionSettings.Current`. One-time
migration: each main-soundtrack `TimelineAudioClip` → an `[AudioClip]` op with `AutoPlay = true` +
`DisplayAs = BackgroundImage`. Then delete `TimelineAudioClip` + the `CompositionSettings` audio storage. This is
the load-bearing part the user scoped out of the initial cut; it's its own project.

Docs (`.help/docs/using/`) + manual test sets land with the phases they describe.

## Open questions / deferred

1. **Double-registration** when a clip is both `AutoPlay` and collected by a player — does `AudioEngine`'s
   handle-keyed `SoundtrackClipStreams` already dedup within a frame, or must the player skip `AutoPlay` clips?
   (Prototype in Phase 2.)
2. **Per-clip volume / mute conveyance** — `UseSoundtrackClip` takes a handle + time; confirm where per-clip
   `Volume` / `Mute` feed the stream (the engine multiplies channel volume today — see `SoundtrackClipStream`).
3. **Source trim semantics** — map `SourceOffsetSecs` / `SourceDurationSecs` (seconds) onto the op's
   `TimeClip.SourceRange` (bars). Video does the bars↔seconds remap in `VideoClip.Update`; audio plays at native
   rate, so the offset is a seek into the file, not a rate change. Define the mapping explicitly before locking inputs.
4. **Per-frame scan cost — RESOLVED (2026-06-09).** `AudioClipCollector` and `[AudioClipPlayer].AutoCollect`
   no longer walk `Children.Values` every frame. New Core field **`Symbol.VersionCounter`** mirrors the editor's
   `SymbolUi.VersionCounter`, forwarded in `SymbolUi.FlagAsModified` (which every child add / remove / copy-paste
   already calls — verified). Each scanner caches its filtered provider list, rebuilding only when the composition
   **instance** changes (focus switch / hot-reload) or its `Symbol.VersionCounter` bumps (any edit). In the Player
   there's no `SymbolUi`, so the mirror stays 0 and the static graph builds the list once. **`_ProcessVideoClips`'
   AutoCollect scan (`Plan_VideoClipPlayer.md` Phase 3) can now adopt the same `Symbol.VersionCounter`** — the
   shared infra is in place. (Chosen over a Core-side `AddChild`/`RemoveChild` bump because forwarding the existing
   UI signal also catches copy-paste and other non-`AddChild` mutations.)
5. **Multiple `AutoCollect` players** in one composition each register all clips — document or scope to a layer range.
6. **The `IsMainSoundtrack` overload** (background draw + FFT + export bundled in one flag) is the thing Phase 4
   has to unbundle; `DisplayAs` is the first step (separates the *draw* concern from the others).

## Manual test sets

- `audio-clip-player-autoplay.md` (Phase 1) — place an `[AudioClip]`, verify play / drag / trim / **split** /
  snap / delete / mute / waveform.
- `audio-clip-player-collect.md` (Phase 2) — `AutoPlay = false` + wired / `AutoCollect`; dedup.
- Extend `recording-audio.md` (Phase 3) — recorded take is an `[AudioClip]` op; session undo.
