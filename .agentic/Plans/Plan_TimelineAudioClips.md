# Timeline audio clips

Replaces the abandoned `Plan_AudioEngineTimelineClips.md`. Sub-plan slotted into [`Plan_LiveSessionRecording.md`](Plan_LiveSessionRecording.md) between Phase 1 (audio capture) and the recording-feature UI work (Phase 3+).

## Goal

Audio clips on the timeline are **first-class symbol-level entities**, edited directly in `LayersArea`. No operator instance is required to place a clip. The existing `[AudioPlayer]` and `[PlayAudioClip]` ops continue to cover trigger-based / graph-driven playback unchanged — this plan does not touch them.

This is Option 3 from the design discussion: model timeline audio after a non-linear editor (Audacity / Final Cut / Ableton), not as a graph operator. The graph stays focused on programmable / animated audio behaviour; the timeline stays focused on non-linear placement of audio content.

## Architectural decisions (locked in)

- **First-class clips, no op required.** Clips live in `composition.Settings.Playback.AudioClips`. `LayersArea` reads from there and renders them.
- **Rename `SoundtrackClipDefinition` → `TimelineAudioClip`.** The old name no longer fits.
- **`FilePath` → `AssetPath`.** Aligns with the wider asset-path naming convention.
- **`StartTime` / `EndTime` → `TimeRange`.** Existing fields are already in bars; this is a structural cleanup.
- **Drop `Bpm` from the clip.** BPM lives only in `Playback.Bpm`. Migration on load: if any incoming clip carries `Bpm` and `Playback.Bpm == 0`, copy it once and discard the per-clip field.
- **Drop `DiscardAfterUse`.** Engine manages stream lifetime via stale-thresholding, not per-frame flagging. Avoids thrash when a clip moves in and out of the playhead range during scrub.
- **Audio plays at NATIVE rate** (Option β from the design discussion). `TimeRange` is the timeline window in bars; the clip carries `SourceOffsetSecs` and `SourceDurationSecs` to describe what part of the file is audible. **BPM changes never pitch-shift the audio.** Stretching the clip in `LayersArea` trims/extends the audible *window*, not the playback rate. Stretched-indicator visual stays useful — it now signals "TimeRange doesn't match audio length" rather than "audio is being time-stretched."
- **`IsMainSoundtrack` is transitional.** Today bundles three concerns (background waveform render + FFT routing + export pipeline). Stays in this plan; the unbundling is acknowledged technical debt for a later refactor.
- **DataClips use a different model.** Library + placement (`DataClipDefinitions[]` + `DataClipPlacements[]`) so the same MIDI loop can appear at multiple times. Designed in `Plan_LiveSessionRecording.md` Phase 3 — not in this plan.
- **Inspector in the Parameter window.** Selecting a clip surfaces editable fields there (file path, volume, etc.). Matches how operators expose their parameters; users already look there.
- **Drag-drop target moves from graph to LayersArea.** A `.wav` dropped on the graph today creates an `AudioClip` op (we shipped that in Phase 2a, now superseded). After this plan, drop targets `LayersArea` and creates a new entry in the clip array at the drop position. Drop on the graph becomes a no-op (or shows a hint).

## Final data model

```csharp
sealed class TimelineAudioClip
{
    public Guid       Id;
    public string?    AssetPath;
    public TimeRange  TimeRange;            // bars — position + duration on the timeline
    public double     SourceOffsetSecs;     // where in the source file playback starts
    public double     SourceDurationSecs;   // optional; 0 means "until file end"
    public int        LayerIndex;
    public float      Volume = 1f;
    public bool       IsMainSoundtrack;     // transitional: waveform-bg + FFT + export

    [JsonIgnore]
    public double LengthInSeconds;          // populated by BASS on load
}
```

JSON reader order: prefer new field names; fall back to old (`FilePath`, `StartTime`/`EndTime`, `Bpm`, `IsSoundtrack`, `DiscardAfterUse`) for back-compat with pre-rewrite projects. Old example `.t3` files auto-migrate to the new schema on first save.

## What gets deleted in this plan

- `Operators/Lib/io/audio/AudioClip.cs` + `.t3` + `.t3ui` — the transitional operator built during Phase 2a.
- The `.wav` → `AudioClip` registration in `Editor/Gui/Windows/AssetLib/AssetHandling.cs`.
- The temporary `Log.Debug` probe in `AudioClip.cs` (goes away with the file).

## Current state — what exists

- [`Core/Audio/SoundtrackClipDefinition.cs`](../../Core/Audio/SoundtrackClipDefinition.cs) — to be renamed and restructured.
- [`Core/Settings/CompositionSettings.cs:259-269`](../../Core/Settings/CompositionSettings.cs) — already has BPM-from-first-soundtrack auto-copy logic; one-shot migration target.
- [`Core/Audio/AudioEngine.cs:252`](../../Core/Audio/AudioEngine.cs#L252) — the `handledMainSoundtrack` privilege guard that needs splitting (playback vs. FFT/export).
- [`Core/Audio/SoundtrackClipStream.cs:157`](../../Core/Audio/SoundtrackClipStream.cs#L157) — `UpdateSoundtrackTime` does bars-math today; needs rewriting for native-rate playback driven by `SourceOffsetSecs`.
- [`Editor/Gui/Windows/TimeLine/TimeLineImage.cs`](../../Editor/Gui/Windows/TimeLine/TimeLineImage.cs) — main-soundtrack background-waveform renderer; stays as-is, only the "which clip" lookup changes.
- [`Editor/Gui/Windows/TimeLine/TimeClips/LayersArea.cs`](../../Editor/Gui/Windows/TimeLine/TimeClips/LayersArea.cs) — today walks op-backed `CompositionTimeClips`; will gain a parallel walk of `Settings.Playback.AudioClips`.
- [`Editor/Gui/Windows/TimeLine/TimeClips/TimeClipItem.cs`](../../Editor/Gui/Windows/TimeLine/TimeClips/TimeClipItem.cs) — today bound to a `SymbolChild`; needs a parallel rendering path for non-op clips.
- [`Editor/Gui/MagGraph/Ui/DropHandling.cs`](../../Editor/Gui/MagGraph/Ui/DropHandling.cs) + [`Editor/Gui/Windows/AssetLib/`](../../Editor/Gui/Windows/AssetLib/) — model for the new LayersArea drop handler; `FileImport.TryImportDroppedFile` already handles asset-copy.

## Phases

### Phase A — `IsSoundtrack` → `IsMainSoundtrack` rename ✅ Done

Already shipped. Behaviour-preserving rename + back-compat JSON reader (`IsMainSoundtrack` preferred, `IsSoundtrack` fallback).

### Phase B — Data model rewrite + native-rate playback math

**Goal:** rename and restructure `SoundtrackClipDefinition` to the final shape; rewrite `SoundtrackClipStream.UpdateSoundtrackTime` for native-rate playback. Single-clip soundtrack projects continue to work without change.

**Scope:**

- Rename `SoundtrackClipDefinition` → `TimelineAudioClip`. Update every reference (mechanical).
- Rename `FilePath` → `AssetPath`.
- Replace `StartTime` / `EndTime` with `TimeRange`.
- Add `SourceOffsetSecs`, `SourceDurationSecs`.
- Drop `Bpm` field.
- Drop `DiscardAfterUse` field.
- JSON: write new field names. Read: prefer new, fall back to old. Migrations:
  - `StartTime` + `EndTime` (bars) → `TimeRange { Start = StartTime, End = EndTime }`.
  - `Bpm` → copy into `Playback.Bpm` if `Playback.Bpm == 0`, then discard.
  - `DiscardAfterUse` → drop silently.
- Rewrite `UpdateSoundtrackTime`: target source-time-secs = `SourceOffsetSecs + (playheadBars - TimeRange.Start_bars converted to secs via Playback)`. No rate scaling. Out-of-bounds check uses `SourceDurationSecs` (or file length).

**Testable outcome:**

- Open each bundled example project that uses a soundtrack (the `.t3` files we left unmigrated in Phase A). Soundtrack plays at native rate. Scrub: re-syncs cleanly. FFT-driven reactions still work. Export-to-video still includes audio.
- Re-save: JSON now uses new field names. Reopen the re-saved project: still works.

**Effort:** ~1 day. Main risk is the time-math rewrite — keep `AudioSyncingOffset` and the resync threshold intact, only change *what* gets seek'd-to.

### Phase C — Engine plays N clips. Split FFT routing.

**Goal:** drop the `handledMainSoundtrack` privilege from playback machinery; retain it only for FFT + export routing.

**Scope:**

- In `AudioEngine.ProcessSoundtrackClips`: every registered clip stream gets `UpdateSoundtrackTime`, not just the first `IsMainSoundtrack` one.
- The `IsMainSoundtrack` flag continues to gate `UpdateFftBufferFromSoundtrack` and `AudioRendering.ExportAudioFrame`. Transitional behaviour; full unbundling is later.

**Testable outcome:**

- Hand-edit a project's `.t3` to add a second `TimelineAudioClip` entry pointing at a different `.wav`. Both clips play simultaneously without interfering. Main soundtrack still drives FFT. Audio export still captures the main soundtrack only (intentional, transitional).

**Effort:** ~half day.

### Phase D — LayersArea renders array-backed clips (read-only)

**Goal:** make array entries visible in the layers area. No editing yet.

**Scope:**

- `LayersArea` walks `composition.Settings.Playback.AudioClips` in addition to the existing `CompositionTimeClips`. Each entry rendered at its `TimeRange` and `LayerIndex`.
- Visual distinction from op-backed TimeClips (different fill / border) so users can tell them apart at a glance.
- Hover tooltip: `AssetPath`, duration, volume.
- Background waveform image populates lazily via `AudioImageFactory` (already exists; extend to all clips, not just the main soundtrack).

**Testable outcome:**

- Two clips in JSON: both visible at correct positions + layers. Waveforms appear in clip bodies within a few seconds. Visual distinguishes them from op-backed clips.

**Effort:** ~1 day. Most of the work is a `TimeClipItem`-equivalent renderer for the non-op case.

### Phase E — LayersArea editing + drag-drop replaces the op

**Goal:** clips become user-creatable and -editable from the timeline. Old `AudioClip` op is removed.

**Scope:**

- Selection: clicking a non-op clip selects it. Selection state lives alongside existing clip selection but is distinguishable.
- Commands (all through `UndoRedoStack.AddAndExecute`):
  - `AddTimelineAudioClipCommand`
  - `MoveTimelineAudioClipCommand`
  - `TrimTimelineAudioClipCommand` (start- and end-handle variants)
  - `DeleteTimelineAudioClipCommand`
- Drag-drop `.wav` onto `LayersArea` creates a clip:
  - `TimeRange` inferred from drop X + the file's natural duration in bars at current BPM.
  - `LayerIndex` from drop Y.
  - File copied into `Assets/audio/` via `FileImport.TryImportDroppedFile`.
- Single-clip move/trim/delete only in v1. Multi-select, copy-paste, snapping → follow-up phase.
- **Delete the old AudioClip op:** remove `Operators/Lib/io/audio/AudioClip.cs` + `.t3` + `.t3ui`. Remove the `.wav` → `AudioClip` registration from `AssetHandling.cs`. Drop on the graph for `.wav` becomes a no-op (or shows an inline hint pointing at LayersArea).

**Testable outcome:**

- Drop a `.wav` on LayersArea: clip appears at drop point. Plays in sync when the playhead crosses it.
- Drag clip body: clip moves. Hit handles: clip trims. Delete key: clip removed. Each action undoable individually.
- Symbol Browser no longer shows `AudioClip`. Asset library still lists `.wav` files. Dropping a `.wav` on the graph does *not* create an op.

**Effort:** ~2-3 days. Biggest phase. The selection + command machinery for non-op clips is genuinely new code paths.

### Phase F — Parameter inspector + cleanup

**Goal:** clip parameters editable via the Parameter window.

**Scope:**

- When a non-op clip is selected in `LayersArea`, the Parameter window shows its inspector. Fields:
  - `AssetPath` (file picker, `.wav`/`.mp3`/`.ogg` filter)
  - `Volume` (slider 0..1)
  - `SourceOffsetSecs` (numeric)
  - `SourceDurationSecs` (numeric, 0 = until file end)
  - `IsMainSoundtrack` (toggle — exclusive; turning on demotes the previous main)
- Edits go through commands (`ChangeTimelineAudioClipParameterCommand` or similar) so undo/redo works.
- Update [`.agentic/SOLUTION_OVERVIEW.md`](../SOLUTION_OVERVIEW.md) drag-drop section: audio files now target `LayersArea`, and the `.wav` registration in `AssetHandling.cs` has been removed.
- Update [`Plan_LiveSessionRecording.md`](Plan_LiveSessionRecording.md): Phase 2 description rewritten to reference this plan as completed; Phase 3 (DataClip design) still pending its own pass.
- Add [`.tests-manual/audio-clip-playback.md`](../../.tests-manual/audio-clip-playback.md) covering drop / move / trim / delete / inspector / scrubbing / main-soundtrack-coexistence.

**Testable outcome:**

- Select clip; Parameter window shows fields. Edit volume; immediate audible change. Edit asset path; clip switches source. Toggle `IsMainSoundtrack`: clip becomes the background waveform; previous main demotes.
- Manual test set passes end-to-end.

**Effort:** ~1 day.

## Documentation updates

- [`.help/docs/using/Timeline.md`](../../.help/docs/using/Timeline.md) — new "Audio clips" section: drop-on-LayersArea flow, the inspector, the difference between an audio clip on the timeline and an `[AudioPlayer]` op in the graph.
- [`.help/docs/using/LivePerformances.md`](../../.help/docs/using/LivePerformances.md) — cross-link.
- [`.agentic/SOLUTION_OVERVIEW.md`](../SOLUTION_OVERVIEW.md) — drag-drop section updated to reflect that audio files target the timeline clip area (`ClipArea`), not the graph.

## Manual test sets

- `audio-clip-basic-playback.md` — drop, move, trim, delete, scrubbing.
- `audio-clip-coexistence.md` — multiple clips, same and different layers; main-soundtrack + non-main mix.
- `audio-clip-soundtrack-migration.md` — open an old project with `IsSoundtrack` / `StartTime` / `EndTime` / `Bpm` / `DiscardAfterUse` in JSON, verify it loads and plays unchanged. Re-save, verify JSON uses new field names. Reopen, still works.
- `audio-export.md` — render a project with the main soundtrack to file; audio is in the output. (Phase B/C regression net.)

## Open questions / deferred

1. **FFT routing for non-main clips.** After this plan, only the main soundtrack drives FFT; secondary clips are silent on `[AudioReaction]` etc. Acceptable for v1; the unbundling is a separate plan.
2. **Looping.** A clip's `TimeRange` longer than `SourceDurationSecs` results in silence after the audio ends. A `Loop` flag is a natural follow-up but not in this plan.
3. **Copy-paste, multi-select, snapping** for non-op clips. Follow-up after v1.
4. **`Resource<>` pattern for clips.** The current `Resource<SoundtrackClipDefinition>` wrapper (in `PlayAudioClip` and the doomed `AudioClip` op) does file-watching and reload-on-change. The new model doesn't need it — clips are pure data, the engine loads BASS streams as needed. Asset resolution (`AssetRegistry.TryResolveAddress`) is still used at load time; only the `Resource<>` wrapper goes away from the deleted op paths.
5. **DataClips.** Library + placement model. Designed in `Plan_LiveSessionRecording.md` Phase 3.
6. **`PlayAudioClip` and `[AudioPlayer]` ops** — unchanged in this plan. They cover the interactive / graph-driven use case the user reaches for via the graph, not the timeline.

## Follow-up feature wishlist (post-v1)

User-captured feature ideas to consider after v1 ships. These are not committed to a phase yet — group/scope them into a follow-up plan once priorities are clear.

### Editing parity with op-clip / operator UX
- **Mute / disable.** Unify with the operator "disable" toggle for selected items. For a mixed selection of clips (`TimelineAudioClip`, `TimeClip`, future `VideoClip`, …) pressing the disable shortcut:
  - disables all if any are still enabled,
  - enables all if all are already disabled.
  Mute and Disabled become synonymous for audio clips.
- **Trim to playhead.** Drag-free shortcut: snap selected clip's Start or End to the current play time, clamped to available source content.
- **Cut at playhead.** Split selected clips at the current play time (mirrors the existing op-clip `SplitClipsAtTime`).
- **Duplicate.** Duplicate selected clips with `LayerIndex` incremented so the copy lands on the row below.
- **Rename.** Per-clip display name; falls back to asset filename when empty.
- **Source-range indicator.** While hovering or dragging body/start/end, draw a ghost outline showing the maximum available content range ("flesh") so the user can see how much more can be revealed by extending.

### Controlling audio from the graph
- **`ControlTimelineAudioClip` operator.** References a `TimelineAudioClip` by Guid (dropdown in inspector), drives volume, panning, mute, and (long-term) BASS-effect parameters. Needs to evaluate per-frame to feed the engine.

### Long-term ambitions
- **Clip markers / bookmarks.** Per-clip waypoints with small text labels. Use case: local Whisper / cloud transcription writes SRT data back as markers for voice-over alignment.
- **Audio pipeline routing.** Named outputs, compressor/effect chains, sidechaining (ducking) between clips.
- **Clip linking / groups.** Link a set of clips so move/trim/delete propagate together. Especially useful for live-event recordings where an audio clip and its companion IO/data clips must stay in sync.

### DataClip editing (relates to `Plan_LiveSessionRecording.md` Phase 3)
- Extend the IO Window to edit captured data:
  - **events** — select, delete, move, edit, quantize, filter / clean up.
  - **channels** — per-channel inspection and editing.
