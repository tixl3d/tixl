# Load MIDI files as data-clips

Ticket: #1082 — https://github.com/tixl3d/tixl/issues/1082
Size: —   Milestone: v4.2

## Problem
Dropping a `.mid`/`.midi` file on the graph should convert it into a TiXL `DataSet` (data-clip), so MIDI
files can drive the graph like recorded data clips. Needs a new asset type for the extensions and an
operator that parses the file (NAudio) into a DataSet.

## Affected code (infrastructure already exists)
- DataSet model: `Core/DataTypes/DataSet/DataSet.cs` — channels with hierarchical `Path`, `Type`,
  `DurationType` ("Tick" point / "Interval" span), events `DataEvent` / `DataIntervalEvent`, times in seconds.
- MIDI→DataSet conversion reference: `Core/DataTypes/DataSet/MidiDataRecording.cs:57-119` — channel paths
  like `["Midi","<device>","Ch<n>","N60"/"CC74"/"PB"]`; NoteOn/Off → interval events, CC → tick events.
- Asset type + extension registration: `Editor/Gui/Windows/AssetLib/AssetHandling.cs:30-179`
  (see the existing "Data" type at ~93-104 mapping `.data` → `LoadDataClip`), `Core/Resource/Assets/AssetType.cs`,
  `Core/Resource/Assets/FileExtensionRegistry.cs`.
- Drop routing: `Editor/Gui/MagGraph/Ui/DropHandling.cs:24-192` (wires the dropped asset's path into the
  operator's first string input), `Editor/Gui/UiHelpers/FileImport.cs`.
- Operator template: `Operators/Lib/io/data/LoadDataClip.cs` — string `FilePath` input, `Resource<DataSet>`
  lazy-load, `TimeClipSlot<DataClip?>` output. Copy this shape.
- NAudio MIDI parsing already available (`NAudio.Midi 2.3.0` in Core.csproj); usage example in
  `Operators/Lib/io/midi/MidiClip.cs:108` (`new MidiFile(path)`, `DeltaTicksPerQuarterNote`, `Events`).

## Proposed approach
1. New op `Operators/Lib/io/midi/LoadMidiClip.cs` (new GUID), modelled on `LoadDataClip`: string `FilePath`
   input, `Resource<DataSet>` load, `TimeClipSlot<DataClip?>` output.
2. In the loader, parse with NAudio `MidiFile` and convert tracks/events into a DataSet using the
   `MidiDataRecording` channel-path conventions (reuse/extract that logic so record and load agree).
3. Register a "Midi" asset type in `AssetHandling.InitAssetTypes()` for `mid`/`midi` → the new op GUID;
   pick subfolders (e.g. `["midi"]`). Drop routing then works for free.

## Risks / side-effects
- **Time units** are the main trap: MIDI events are ticks; recording uses absolute seconds; TimeClip wants
  bars. `MidiClip.cs` shows tick→bar conversion. Pick one convention (match `MidiDataRecording` seconds for
  DataSet consistency) and document it.
- Keep MIDI→DataSet conversion shared between record and file-load to avoid drift.

## Open questions (ticket author is explicitly unsure)
- How to surface notes from the DataSet downstream — `MidiInput` is device-based, not note/dataset-based;
  may want a generic "data device" or a MIDI-playback operator. Decide the consumption story before
  finalizing the channel schema.
- Channel naming/grouping: per-track channels, track prefix, include CC/PB/aftertouch or notes only?
- Should there also be a `MidiPlayback` operator, or is the DataSet output enough for now?
- Interface-stability audit (per AGENT_INSTRUCTIONS) before locking the DataSet schema for MIDI.
