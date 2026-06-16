# Live Session Recording & Playback

## Goal

Let users record live performances during playback — audio (mic / WASAPI loopback) and IO events (MIDI, OSC) — so the recorded session can be played back, scrubbed, and edited as part of the timeline. Recorded audio appears as a streamable `AudioClip` TimeClip; recorded IO data appears as a `DataClip` TimeClip whose contents can either feed downstream operators directly or be re-injected as a virtual MIDI/OSC device.

## Architectural decisions (already discussed)

These are locked-in before phasing:

- **Recording start time** defaults to *current playback time*, with an opt-in "Start at 0" mode.
- **Audio and data are separate TimeClips**, not one merged clip. Independent trim/move/disable.
- **Overdub model**: during recording, existing `DataClips` overlapping the record range get their op output flagged `IsDisabled = true` (re-uses `SymbolChild.Output.IsDisabled`, persists, undoes through the existing mechanism). `AudioClips` stay audible. New clips stack on `maxLayerIndex + 1`.
- **`SimulateIoData`** is built on top of an explicit operator output (`DataClip` → graph), with an opt-in "Inject as device" toggle that registers a virtual device through `MidiInConnectionManager` / `OscConnectionManager`.
- **Toolbar rename**: `UiElements.DrawProjectControlToolbar()` becomes a dedicated `TimelineToolbar` file at the end of the feature — small rename commit, not bundled with logic.
- **Loop boundaries**: recording stops when the playhead reaches the loop end; the user gets a Console notice. Loop-aware overdubbing (wrap-and-keep-recording) is explicitly out of scope — flagged as a future increment.
- **Layer assignment**: both new clips go on `maxLayerIndex + 1` (audio and data on the same fresh layer pair, audio above data). Revisit after first real session — may need separate "always lowest / always highest" rules per type.
- **Multiple audio sources**: each WASAPI source records to its own WAV and lands as its own `AudioClip`, on adjacent new layers. No mixing at record time; merge is a later authoring action.
- **IO channel scope**: record *everything* that arrives from the selected MIDI/OSC sources during the record window. No per-channel filter at record time; filter at playback if needed. Debug logging output is excluded by definition (it's not on those managers' event streams).
- **File layout**:
  - Audio → `Assets/audio/`
  - Recorded IO data → `Assets/dataclips/` with a new `.data` extension (a `FileType` registration alongside existing types). Internals stay the current `DataSet` JSON for v1 — can revisit a compact binary format later without changing the extension.
  - Naming uses a **shared session index**: on record start, scan both `Assets/audio/` and `Assets/dataclips/` for the highest existing `rec-NNN` and use `NNN+1`. Files within one session: `rec-007.data`, `rec-007-mic1.wav`, `rec-007-loopback.wav`. Audio-only and data-only sessions still bump the same counter so indices stay aligned.
- **Version control**: recordings are tracked normally with the rest of `Assets/`. Rationale: the "minimal" backup strategy already excludes binary blobs from automatic backups; most users don't have a git repo at all; users who do can add `*.data` and `*.wav` to their own `.gitignore` if they want.

## Current state — what exists

- [`Core/Audio/WasapiAudioInput.cs`](../../Core/Audio/WasapiAudioInput.cs) captures mic / loopback samples for FFT, but never writes them to disk. The hook for capture exists; the WAV writer does not.
- [`Core/Audio/SoundtrackClipDefinition.cs`](../../Core/Audio/SoundtrackClipDefinition.cs) + `AudioClipResourceHandle` load via BASS but **fully into memory** (no `BASS_STREAM_MEM` flag is set today; resources are not streamed from disk during playback). For long recordings this needs to switch to BASS streaming.
- [`Editor/Gui/Audio/AudioImageGenerator.cs`](../../Editor/Gui/Audio/AudioImageGenerator.cs) renders waveform PNGs offline via BASS — reusable as-is for recorded WAVs, just invoke it after `stop`.
- [`Core/Animation/TimeClip.cs`](../../Core/Animation/TimeClip.cs) + [`Editor/Gui/Windows/TimeLine/TimeClips/LayersArea.cs`](../../Editor/Gui/Windows/TimeLine/TimeClips/LayersArea.cs) render and edit TimeClips with `LayerIndex` already implicit.
- [`Core/DataTypes/DataSet/DataSet.cs`](../../Core/DataTypes/DataSet/DataSet.cs) has `DataChannel` + `DataEvent` + `DataIntervalEvent` and JSON serialization. Right shape for IO capture, zero design work needed.
- [`Core/IO/MidiInConnectionManager.cs`](../../Core/IO/MidiInConnectionManager.cs) and [`Core/IO/OscConnectionManager.cs`](../../Core/IO/OscConnectionManager.cs) expose consumer/listener registration — the seam for virtual playback devices.
- Toolbar lives at `UiElements.DrawProjectControlToolbar()` (to be renamed).

What is **missing**: WAV writer, streaming AudioClip playback, DataSet recorder, `DataClip` operator, `SimulateIoData` operator, record button + settings popup, timeline rendering for data density, muted-clip visual in `LayersArea`.

---

## Phase 1 — WAV streaming writer (audio capture infrastructure)

**Goal:** Continuous, allocation-free WAV write from `WasapiAudioInput` samples to `Assets/audio/`.

**Scope:**
- New `Core/Audio/WavFileWriter.cs` — opens a file, writes a placeholder 44-byte RIFF header, appends 16-bit PCM samples in a fixed-size byte buffer, finalises the header on close. Supports mono/stereo and 44.1/48 kHz selectable at construction.
- Wire `WasapiAudioInput` to optionally feed a `WavFileWriter` instance per active capture. No per-sample allocations — samples are read from BASS into a reusable buffer and written through.
- File naming follows the shared-session-index convention from the decisions section: `Assets/audio/rec-NNN.wav` for a single source, `Assets/audio/rec-NNN-<sourceName>.wav` when multiple sources are active. Index resolution scans both `Assets/audio/` and `Assets/dataclips/` so future Phase 3 data files share the counter. Phase 1 only writes audio, so the scan is half-populated until Phase 3 lands — that's fine.
- Dev-only entry point for verification: a debug menu item or a temporary keybind that toggles capture on/off. **Not** the real record button — that's Phase 4.

**Testable outcome:**
- Trigger the debug command, speak / play audio for 10 s, trigger again.
- WAV file exists at the expected path, opens in Audacity, plays back with no glitches, correct duration, correct sample rate, no clipping when input is normal.
- Stop test: kill the editor mid-recording. The WAV's header is corrupt (expected — header is finalised on close), but the recovery path (or at least a clear log warning) is in place.

**Effort:** ~1–2 days. Mostly RIFF wrangling and threading discipline.

**Risk:** WASAPI capture format may differ from the writer's expected layout (interleaved vs planar, int16 vs float32). Resolve early — add a unit-style sanity log on first sample of a session.

---

## Phase 2 — Streaming `AudioClip` playback + recorded files on timeline

**Goal:** Long WAVs play back without loading fully into memory; recorded files can be placed as `AudioClip` TimeClips and show waveform thumbnails.

**Scope:**
- Modify the BASS load path in `AudioClipResourceHandle` / `SoundtrackClipDefinition` to use streaming flags (`BASS_StreamCreateFile` without `BASS_STREAM_MEM`). Verify seeking still works for scrub.
- Add a "drop recorded WAV onto timeline" action — the file from Phase 1 becomes an `AudioClip` op with `TimeRange.Start` = current playhead. Reuse whatever drag-drop / asset-browse path AudioClip already supports if present; otherwise add a minimal "Insert as AudioClip at playhead" action.
- After file close in Phase 1's writer, kick off `AudioImageGenerator.TryGenerateSoundSpectrumAndVolume()` on a background worker. Show the waveform in the TimeClip body when ready (same code path that already paints soundtrack waveforms).
- No record button yet — files are dropped in manually.

**Testable outcome:**
- Record a 5-minute WAV via Phase 1's dev trigger.
- Drop it onto a layer; an `AudioClip` appears. Scrub through; audio plays at the playhead position; the waveform image appears within a few seconds.
- Memory: confirm via Task Manager that the editor does *not* allocate ~50 MB extra per minute of clip duration (proves streaming works). A 30-minute WAV should add roughly tens of MB, not hundreds.

**Effort:** ~2–3 days. The streaming switch is small but every existing soundtrack usage needs a regression pass.

**Risk:** Existing soundtracks may rely on in-memory access for BPM detection or other one-shot reads. Inventory call sites before flipping the flag globally; if needed, gate streaming behind a per-clip flag and default it on for recorded files only.

---

## Phase 3 — DataSet recording + `DataClip` / `SimulateIoData` operators

**Locked design (2026-05 review):**

- The file-loading op is **`LoadDataClip`** (a `[TimeClip]` operator). No symbol-level `DataClipDefinition` — that was rejected. The op carries a `FilePath` input and loads via the existing `Resource<>` machinery, so file-watch + hot reload come for free. It outputs a single `DataClip` value (see below).
- **`DataClip`** is a new **value type** in `T3.Core.DataTypes.DataSet` — wraps a `DataSet` plus an optional `TimeRangeMapping`. It's the wire-carried type downstream ops consume; `DataSet` stays meaningful for ops that don't need timeline context (mean of a channel, energy sum, etc.).
- **`TimeRangeMapping`** is a shared `readonly struct` in `T3.Core.Animation` — captures `(TimeRange, SourceRange, Bpm)` and exposes `LocalBarsToSourceBars` / `LocalBarsToSourceSecs` / `IsActive`. Single source of truth for the playhead-mapping math every TimeClip-based op performs. Existing ops (AudioClip, VideoClip, ImageSequenceClip, MidiClip) will opportunistically adopt it when next touched.
- Deserialization (`.data` JSON → `DataSet`) happens **once per asset path**, cached in a static `Dictionary<string, DataSet>` helper. Multiple `DataClip` ops referencing the same file share one parsed `DataSet`. Eviction tied to `Resource<>` invalidation.
- One `.data` per recording session — all enabled MIDI/OSC sources merge into a single `DataSet` (its existing channel model already supports that). No per-source split for data, unlike audio.
- Live op creation during recording. `DataClip` op spawned on record-start, `TimeRange.End` extended each frame (mirrors audio).
- `SimulateIoData` injects under the **real device's name** — no `(playback)` suffix, no override toggle. Real + simulated event streams merge on the consumer side. "Simulate" means *be* the device, not impersonate it.
- **Variation / snapshot controller MIDI** capture-side is already handled by Phase 3a: `IoDataSetRecorder` registers as an independent `IMidiConsumer`, so `MidiConnectionManager` fans every real device event out to it alongside `CompatibleMidiDevice` (which drives the snapshot recalls). The control-mode flag (`MidiConnectionManager.SetDeviceControlMode`) only suppresses passthrough to the `MidiInput` op via an opt-in check inside that op — it doesn't gate the recorder.
- **Variation / snapshot replay (Phase 3c work).** Injecting events back through `MidiInput` alone is not enough — variations are driven by `CompatibleMidiDevice` instances, which are *separate* `IMidiConsumer`s. For a recorded snapshot trigger to fire on playback, `SimulateIoData` must fan the event out to **all** registered MIDI / OSC consumers (including `CompatibleMidiDevice`). Add a `MidiConnectionManager.BroadcastSimulatedMessage(sender, args)` and the equivalent on `OscConnectionManager`:
  - The `sender` must be the live `MidiIn` whose name matches the recorded device, so `CompatibleMidiDevice.GetDescriptionForMidiIn` resolves to the right device descriptor. If the original device is disconnected, log a warning and skip the event.
  - Mark broadcast args with an `isSimulated` flag so `IoDataSetRecorder` (if a session is concurrently active) can skip-record them and avoid feedback loops.
- Drag-drop unification via `AssetType` registration:
  - `.data` registered as a new `AssetType` whose `PrimaryOperators` includes the `DataClip` op Guid.
  - Drop on graph → standard `AssetType` flow creates a `DataClip` op at cursor with `FilePath` pre-filled. Free.
  - Drop on clip area → needs a small generalisation of the existing audio-only drop handler so any `AssetType` whose primary op is a `[TimeClip]` creates the op as a SymbolChild on a layer. Out of scope for 3a–3d, lands as 3e or a follow-up.


**Goal:** IO events (MIDI, OSC) can be recorded into a `DataSet`, saved per project, and played back through either a graph output or a virtual input device.

**Scope:**
- `Core/IO/DataSetRecorder.cs` — registers as consumer on `MidiInConnectionManager` / `OscConnectionManager`, captures incoming events into a `DataSet` with timestamps relative to record-start. Allocation-free per event.
- Register a new `.data` `FileType` (alongside the existing audio / image / etc. file-type entries) pointing at `Assets/dataclips/`. DataSet serialisation writes `Assets/dataclips/rec-NNN.data` on stop, reusing the existing `DataSet.WriteToFile()` JSON internals under the new extension.
- New `DataClip` operator (in `Operators/Lib/io/`) — wraps a `DataSet` file reference, exposes a `DataSet` output that emits the slice between the previous frame and the current frame for its TimeClip's mapped source time. Acts as a TimeClip so it appears on a layer.
- New `SimulateIoData` operator — takes a `DataSet` input, has an `Inject as device` bool. When on, registers a virtual `MidiIn` / `Osc` consumer source through the existing managers under a name like `<OriginalDevice> (playback)`; when off, only routes through the operator graph.
- (Defer `GetNumberData` to a later increment — `DataClip → SimulateIoData` is the primary path.)
- During recording, set `Output.IsDisabled = true` on `DataClip` ops whose `TimeRange` overlaps the record range (Phase 4's responsibility to *trigger* this, but the visual treatment in `LayersArea` lands here so it can be reviewed in isolation: render disabled TimeClips desaturated with a hatched fill).

**Testable outcome:**
- Trigger a debug "record IO" command, play notes on a MIDI controller for 10 s, trigger again.
- A `.json` DataSet file is written. Drop it onto a layer; a `DataClip` op appears.
- Wire `DataClip → SimulateIoData` (Inject = true). An existing `MidiInput` op in the graph wired to the original device receives the recorded notes during playback at the correct times.
- Toggle Inject = false. The same `MidiInput` op stops receiving notes; the `DataClip`'s `DataSet` output is still consumable downstream.
- Disabling a `DataClip` (graph-side toggle) renders it desaturated in `LayersArea` and stops it emitting events.

**Effort:** ~3–4 days. The injection plumbing through `MidiInConnectionManager` is the unknown — needs a careful look at how consumers vs. devices are modelled there.

**Risk:** Device-name collisions. If the user is monitoring the real `LaunchpadX` and a `SimulateIoData` injects under the same name, what does a downstream `MidiInput` op resolve to? Default to suffixing `(playback)` but keep an "override real device" advanced toggle for live-show scenarios where the controller isn't physically present.

---

## Phase 4 — Record button, settings popup, session orchestration

**Goal:** End-to-end recording from the UI: one click captures audio + IO into clips placed on the timeline, with visible growth during capture.

Sub-phases:
- **4a — Toolbar refactor.** ✓ `UiElements.DrawProjectControlToolbar` moved to `Editor/Gui/Windows/TimeLine/TimelineToolbar.cs`. Behaviour-preserving. Caller in `GraphWindow` updated.
- **4b — Basic record button.** ✓ Toggle on the timeline toolbar drives `WasapiAudioInput.BeginRecording` + `IoDataSetRecorder.BeginRecording` in lockstep. Visual: pulsing red filled circle while recording, hollow outline at rest. The button is a **draw-list placeholder** — no `Icon.Record` glyph exists in the atlas yet; flag for icon addition before shipping.
- **4c — Settings popup.** Gear icon → audio source dropdown, MIDI/OSC source multi-select, start-mode radio. Greyed-but-openable during active recording.
- **4d — Live op creation + live growth during record.** ✓ `RecordingSession.Start` creates the `LoadDataClip` op and appends a `TimelineAudioClip` immediately, both with zero-width `TimeRange` at the record-start bar. `RecordingSession.OnFrame` (called from the toolbar's per-frame draw) extends both clips' `TimeRange.End` by wall-clock elapsed time converted to bars at the current BPM. `RecordingSession.Stop` finalises file paths via `ChangeInputValueCommand` / direct AssetPath mutation. Whole session lands as one `MacroCommand` for unit undo.
- **4e — Stop / finalize.** ✓ Effectively merged with 4d minimal. Outstanding: trigger `AudioImageFactory` waveform generation for the new audio file so the clip body shows its waveform immediately (currently only happens on first BASS load).
- **4f — DataClip body rendering.** ✓ `DataClipBodyRenderer.TryDraw` hooks into `TimeClipItem.DrawClip` after the body fill. For any op whose output is `Slot<DataClip?>`, it iterates the DataSet's channels and draws per-event tick lines positioned via `TimeRangeMapping.SourceSecsToLocalBars`. Density threshold: above ~0.3 events/pixel and >200 events total, fall back to a single faded overlay rect instead of individual ticks (avoids drawlist bloat at 30 Hz CC streams). Zero cost for non-DataClip ops via early-return on output type check.

**Scope (legacy plan body, kept for reference):**
- Refactor `UiElements.DrawProjectControlToolbar()` into a new `Editor/Gui/Windows/TimeLine/TimelineToolbar.cs` (rename + move, otherwise behaviour-preserving — small standalone commit at the start of this phase).
- Add a record toggle icon to the toolbar. While recording: paint red, pulse via the shared `Blink` source.
- Add a gear icon next to it that opens a small popup:
  - **Audio source** — dropdown of WASAPI capture endpoints (mic + loopback).
  - **MIDI inputs** — multi-select list of currently registered MIDI devices.
  - **OSC inputs** — multi-select list of currently registered OSC ports.
  - **Start mode** — radio: `At playhead` (default) / `At zero`.
  - During an active recording, the popup is rendered read-only (greyed but openable).
- On record start:
  - Spawn a `WavFileWriter` per selected audio source (Phase 1).
  - Spawn a `DataSetRecorder` covering the selected MIDI/OSC sources (Phase 3).
  - Create the destination `AudioClip` and `DataClip` TimeClip ops on `maxLayerIndex + 1` with `TimeRange.Start` = record-start, `TimeRange.End` = same value, and extend `End` each frame to the live playhead. Clips remain valid (trimmed) if the user hits stop early.
  - Walk existing `DataClips` whose `TimeRange` overlaps the record range and set `Output.IsDisabled = true` via the existing `SetDisabled` path (one `MacroCommand` so the whole record session undoes as a unit).
- On record stop:
  - Close WAV writers; trigger waveform image generation (Phase 2).
  - Serialise DataSets to JSON (Phase 3).
  - Finalise TimeClip references to the written files. Push the whole session onto the undo stack as one `MacroCommand`.
- TimeClip body rendering for `DataClip`: per-event ticks below a density threshold of ~0.3 events/pixel; above that, a single `AddRectFilled` with alpha proportional to local event density (avoids drawlist bloat).

**Testable outcome:**
- Record button + settings popup behave as described.
- Recording 30 s of mic + MIDI produces two new TimeClips on a fresh layer; pre-existing overlapping `DataClips` go visibly muted; clip bodies fill out in real time.
- Hitting stop, then hitting play, replays the audio (with waveform visible) and the MIDI events (through `SimulateIoData` if user wires it).
- Single Ctrl+Z undoes the entire recording session — clips disappear, muted clips re-enable.

**Effort:** ~4–5 days. Most of the risk is in the live-growth rendering and the macro-command boundary.

**Risk:** Recording across loop boundaries — if the user has timeline loop enabled and the playhead wraps, does the recording wrap or extend? Default: stop recording at the loop point and warn in the Console. Open question to confirm with the user before Phase 4 lands.

---

## Documentation updates (`.help/docs/`)

- **Extend [`.help/docs/using/LivePerformances.md`](../../.help/docs/using/LivePerformances.md)** with a new "Recording live sessions" section covering the toolbar button, settings popup, where files land, and the overdub behaviour.
- **Add [`.help/docs/using/Recording.md`](../../.help/docs/using/Recording.md)** as a focused page on the recording workflow — audio source selection, IO source selection, start-mode semantics, and how to use `DataClip` + `SimulateIoData`. Cross-link from `LivePerformances.md` and `Timeline.md`.
- Touch [`.help/docs/using/Timeline.md`](../../.help/docs/using/Timeline.md) with one paragraph describing the muted-clip visual and the per-event tick density rule, since those affect timeline reading regardless of whether the user records.
- Style per [`.help/docs/STYLE.md`](../../.help/docs/STYLE.md). Each page lands in the same PR as its phase.

## Manual test sets (`.tests-manual/`)

- **`recording-audio.md`** (lands with Phase 4) — `tags: [hardware, essential]`. Steps: enable mic source, start recording, observe growing AudioClip, stop, observe waveform generation, scrub through.
- **`recording-io-data.md`** (lands with Phase 4) — `tags: [hardware, essential]`. Steps: enable MIDI source, record a melody, observe DataClip ticks, hit play, observe events firing through `MidiInput`. Toggle `Inject as device` and verify the difference.
- **`recording-overdub.md`** (lands with Phase 4) — `tags: [essential, edge]`. Steps: with a pre-existing DataClip on the timeline, start recording over it, verify it visibly mutes; stop; verify the new clip stacks above; undo once and verify both clips revert in lockstep.
- A small smoke step belongs in an existing audio-related set if one exists — confirm streaming AudioClip playback didn't regress soundtracks. (To inventory when Phase 2 lands; may end up as a new `audio-streaming.md` set.)

Stale tests get removed if and when a phase is reverted.

---

## Deferred decisions

- **Restoring per-clip `IsDisabled` on undo.** When the record-session `MacroCommand` mutes N overlapping `DataClips` on start, the undo path must restore each clip's *prior* `IsDisabled` value (a clip that was already user-muted before the recording must stay muted on undo). Confirm and, if needed, capture the prior state per clip when the macro is built. Not a phase-1 blocker — surfaces naturally during Phase 3/4 implementation.
