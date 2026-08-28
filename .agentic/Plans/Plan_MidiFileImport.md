# Import MIDI files as timeline DataClips

Ticket: #1082 — https://github.com/tixl3d/tixl/issues/1082
Size: M   Milestone: —

**Status: complete (2026-07-04)** — all phases implemented and verified in-editor, including a
`WasHit` output on both samplers (event-start edge detection; added after testing showed
same-velocity events are invisible in the sampled value). Help page:
[MidiFileImport.md](../../.help/docs/using/MidiFileImport.md). Supersedes the earlier
`Plan_LoadMidiFilesAsDataClips.md` (archived); its open questions are settled by the decisions below.
Manual test set: [midi-file-import.md](../../.tests-manual/midi-file-import.md).

## Problem
There is no way to bring a standard MIDI file (`.mid` / `.midi`) into TiXL. Live MIDI can be recorded
into `.data` DataClips (`MidiRecording`), replayed through `SimulateIoData`, and placed on the timeline
via `LoadDataClip` — but externally authored MIDI (DAW exports, downloaded files) has no import path.

## Goal
1. `.mid`/`.midi` becomes a first-class asset type: drag into the graph or onto the timeline.
2. A `LoadMidiFile` operator parses the file and outputs a `DataClip` as a **TimeClip**
   (placeable / trimmable / splittable on the timeline, like `LoadDataClip`).
3. Imported clips use the **exact channel conventions of live MIDI recording**, so `SimulateIoData`
   can replay them through the simulated MIDI bus and existing `MidiInput`-based patches work unchanged.
4. New sampler operators read channel values directly from any DataClip (MIDI-imported or recorded).

## Key design decisions (settled)
- **Parser: NAudio.Midi** — already referenced in `Core/Core.csproj` (v2.3.0) for live input; its
  `MidiFile` class reads SMF format 0/1. No new dependency.
- **Channel shape = recording convention.** Paths `["Midi", <name>, "Ch<n>", "N<note>"/"CC<cc>"/"PB"/"CP"]`
  per `IoDataSetRecorder.ChannelPaths`. Notes are interval events (DurationType "Interval") carrying
  velocity; CC/PB/CP are tick events. The `<name>` segment (device name for recordings) is the **file
  name** for imports, e.g. `["Midi", "MyTrack.mid", "Ch1", "N60"]`.
- **Raw MIDI value range (0–127), NOT normalized.** `SimulateIoData` casts values straight back to MIDI
  bytes (`Math.Clamp((float)evRef.Value, 0f, 127f)`, see `SimulateIoData.cs:199`); normalized values
  would replay as silence. Normalization is a consumer-side option on the sampler ops.
- **Times in seconds**, converted at import by walking the file's tempo map (`TempoEvent` +
  `DeltaTicksPerQuarterNote` — NAudio does not do this conversion itself). Store source `Bpm` (initial
  tempo) in the DataSet metadata.
- **Sampler ops are named `Sample*FromDataClip`** ("sample at a point in time"), keeping the `Pick*`
  family for stateless selection. Float sampler and gate sampler are **separate ops**; int variant
  only if a use case appears.
- **MIDI + OSC service code moves to a shared `IoServices/` project** (sibling of `VideoServices/`),
  referenced directly by `Lib.csproj` and `Editor.csproj`. MIDI and OSC move together because
  `IoDataSetRecorder` and `SimulatedIoBus` inherently span both — a MIDI-only cut would force an
  awkward seam. No Core factory stubs: the Video split's stub layer exists because of FFmpeg's
  native DLLs + LGPL constraints; NAudio.Midi and Rug.Osc are small managed libs, so direct
  references are fine and Core drops both packages. IO *operators* **may move** to a dedicated
  `Operators/Io` package: symbols are referenced by SymbolId (Guid) only, so package moves don't
  break user projects (precedent: `LoadVideo` moved from Lib to the Video package). Whether to move
  them is a scope decision at Phase 0 time.

## Affected code
- Asset type registration: `Editor/Gui/Windows/AssetLib/AssetHandling.cs` (`InitAssetTypes()`).
- Clip-drop duration probe: `Editor/Gui/Windows/TimeLine/TimeClips/TimelineClipDrop.cs`
  (`ProbeDurationBars`, and the preview probe in `DrawDropPreview` which uses per-type duration caches).
- Reference implementations: `Operators/Lib/io/data/LoadDataClip.cs` (TimeClipSlot + Resource pattern),
  `Core/DataTypes/DataSet/MidiDataRecording.cs` + `IoDataSetRecorder.ChannelPaths` (channel conventions),
  `Operators/Lib/numbers/data/utils/SelectFloatFromDict.cs` (`ICustomDropdownHolder` dropdown pattern),
  `Operators/Lib/io/data/SimulateIoData.cs` (replay consumer — must stay compatible).

## Phases

### Phase 0 — extract IO services (MIDI + OSC) out of Core (standalone refactor) — DONE
Implemented 2026-07-04. Notes from implementation:
- `Core/IO/MidiInConnectionManager.cs` was renamed to `IoServices/MidiConnectionManager.cs` to match
  the class name; the dead duplicate `Core/IO/IMidiConsumer.cs` (unused — all consumers implement the
  nested `MidiConnectionManager.IMidiConsumer`) was deleted.
- `DataChannel`'s constructor and `DataIntervalEvent.Finish` are `internal` to Core; solved with
  `InternalsVisibleTo("IoServices")` (matching the existing `Core.Tests` entry) instead of making
  them public.
- Lib references IoServices with `Private="false" PrivateAssets="all"` (same as Core/SystemUi) so the
  operator package resolves the host's single IoServices.dll — the connection managers are singletons
  and must not be loaded twice across AssemblyLoadContexts. Editor + Player carry real references.
- IO *operators* were NOT moved (optional scope, deferred).
- Residual risk: runtime smoke-test needed — MIDI input op + IO window + recording still share state.
- New shared project `IoServices/` (sibling of `VideoServices/`), owning the NAudio.Midi and
  Rug.Osc package references and these files moved from Core: `MidiInConnectionManager`,
  `IMidiConsumer`, `MidiDataRecording`, `OscConnectionManager` (+ `IOscConsumer`),
  `OscDataRecording`, `IoDataSetRecorder`, `SimulatedIoBus` (the latter two span MIDI + OSC and
  leak NAudio types in their public API, so they move with the interfaces, no translation shims).
- `Lib.csproj` and `Editor.csproj` add a project reference; `Core.csproj` drops NAudio.Midi and
  Rug.Osc.
- Op move done (2026-07-04): new `Operators/Io` package (PackageId fcf9c228-c779-4a14-9395-35324763470e,
  modeled on Video.csproj) holding the hardware-IO slice — `lib/io/{midi,osc,serial,dmx,data}` +
  `Gamepad` (~33 ops). Ops keep their `Lib.io.*` namespaces and folder layout (Video precedent), so
  user-visible paths and Guids are unchanged. Io references IoServices with `Private="false"`
  (host-owned singletons) and declares `lib` + `Types` OperatorPackages deps (two compositions —
  `LinkToMidiTime`, `VisualizeSpotLights` — use Lib children). Examples declares an `Io` dep.
  Lib dropped Rug.Osc, Palink.ArtNet, System.IO.Ports, System.Management. The wider `io/` tree
  (file/json/http/network/audio/video-devices) deliberately stayed in Lib — ~30 non-io Lib symbols
  reference those ops (ReadFile, AudioReaction, MouseInput, FilesInFolder), which would create
  bidirectional package coupling.
- Follow-up done (2026-07-04): serial (`SerialConnectionManager` + System.IO.Ports +
  System.Management) and gamepad (`GamePadInput`, `XInputGamepad` + SharpDX.XInput) moved to
  IoServices as well. Consumers were Lib-only (serial ops, DmxOutput, Gamepad).
- Stays in Core: `DataSet` / `DataClip` / `DataSetCache` (NAudio-free, needed by Player + Editor),
  `DefaultOscPort` in `CoreSettings` (plain data), `BpmProvider` / `TapProvider`
  (playback-coupled).
- Namespace moves touch ~9 MIDI Lib ops, the OSC ops, and Editor IO window / control-surface
  code — mechanical, but verify Player still builds (it consumes SimulatedIoBus transitively via
  Lib's SimulateIoData).

### Phase 1 — LoadMidiFile operator + asset type — DONE
Implemented 2026-07-04: `IoServices/MidiFileToDataSet.cs` (converter + (path, last-write) cache,
writes `SourceDurationSecs` metadata), `Operators/Lib/io/midi/LoadMidiFile.cs` (+ .t3/.t3ui,
Guid b4766419-8bca-4fa0-a398-e6af90ef8971), "Midi" asset type in `AssetHandling.InitAssetTypes()`.
Phase 2 (duration probe incl. drag preview) landed in the same pass in `TimelineClipDrop.cs`.
- New op `Operators/Lib/io/midi/LoadMidiFile.cs`, mirroring `LoadDataClip`:
  `TimeClipSlot<DataClip?>` output, `Resource<DataSet>` load with file-watch invalidation,
  `IStatusProvider`, `IDescriptiveFilename` (filter `*.mid;*.midi`).
- MIDI→DataSet conversion (parser + tempo-map walk) in `MidiServices` (or temporarily in Lib if
  Phase 0 is deferred). Cache parsed DataSets keyed by (path, last-write-time), like `DataSetCache`.
- Register `AssetType("Midi", ["mid", "midi"])` with `PrimaryOperators` and `TimelineClipOperator`
  both pointing at `LoadMidiFile`. Color/icon: match Data type unless a dedicated icon exists
  (ask before adding glyphs).
- Milestone check: drop a `.mid` on the timeline → `SimulateIoData` + `MidiInput` patch plays it.

### Phase 2 — duration probe
- Extend `TimelineClipDrop.ProbeDurationBars` with a MIDI branch (parse, tempo-map, last-event time).
- Optional: a small duration cache for the drag preview, mirroring `AudioClipDurationCache`.
  Note: project BPM converts seconds→bars, so clips from files with a different tempo won't land on
  bar boundaries — same behavior as audio, accepted.

### Phase 3 — sampler operators — DONE
Implemented 2026-07-04: `Operators/Lib/io/data/{SampleFloatFromDataClip,SampleGateFromDataClip}.cs`
(+ .t3/.t3ui) with shared `DataClipSampling.cs` (cached channel resolution, playhead→source-secs
mapping via `clip.Mapping`, binary-search sampling via `DataChannel.FindIndexForTime`). Output
remap was deliberately left out — values stay raw; use [Remap] downstream.
- `SampleFloatFromDataClip`: DataClip input, channel selection via string input with
  `CustomDropdown` usage + `ICustomDropdownHolder` (options = channel paths joined with `/`).
  Samples the last event at-or-before the current time. Default time: playhead mapped through the
  clip's `TimeRangeMapping`; optional `OverrideTime` (float, local clip seconds) gated by a
  `UseTimeOverride` bool (or connected-state). Optional output remap/normalize inputs.
- `SampleGateFromDataClip` (separate op, per decision): bool "is a note active at t" output,
  plus a velocity float output of the active interval event.
- Per-frame constraints: cache resolved channel + last event index; incremental scan / binary
  search, no allocation, no LINQ.

## Risks / side-effects
- Multi-track SMF files can have duplicate channel/CC combinations across tracks; merging tracks
  into one event stream (format-1 standard behavior) may interleave events — must keep per-channel
  event lists time-sorted for the samplers' binary search.
- Running-status and NoteOn-velocity-0-as-NoteOff quirks: NAudio normalizes most of this, verify
  with real DAW exports.
- `TryGetForFilePath` extension collisions: none expected (`mid`/`midi` unused).

## Open questions
- Should note interval events store velocity as value (recording convention — assumed yes)?
  → settled: yes, raw 0–127.
- Per-track channel grouping: SMF format 1 tracks vs MIDI channels — group only by MIDI channel
  (recording convention has no track concept). Track names (MetaEvent) could go into channel
  Metadata for display later.
- Manual test set + `.help/` page to be added with the shipping PR
  (per `.tests-manual/README.md` / docs rules).
- **`[MidiClip]` vs `[LoadMidiFile]` overlap — RESOLVED 2026-08-09.** The old Dict-based `MidiClip` is
  retired: renamed **`[_MidiClip_Old]`** (Guid kept), tagged `Obsolete`, description points to the
  successor. `[LoadMidiFile]` is renamed **`[MidiClip]`** (Guid kept, `AKA: LoadMidiFile` for search) —
  it *is* a clip, and `[MidiClip]→[SampleFloatFromDataClip]` fully replaces
  `[_MidiClip_Old]→[SelectFloatFromDict]`. References updated: sibling op descriptions, AssetHandling /
  TimelineClipDrop comments (Guid refs unchanged), `.help/docs/using/MidiFileImport.md`,
  `.tests-manual/midi-file-import.md`, `time-clip-evaluation.md` step 7. v4.2 release notes left
  historical.
- **Long-term: the Dict flow duplicates DataClip features** (user note 2026-08-09). `Dict<float>` feeds
  many ops (GameController etc.) so it can't be retired soon. Either provide a `DataClip → Dict`
  conversion op or unify the two systems. Park until the DataClip editing work (recording plan Phase 3)
  clarifies the direction.
