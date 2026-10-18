# MIDI Device Aliases

**Status:** Implemented 2026-10-18 (all four phases), uncommitted on `feat/linux-port`. Verified on Linux with a
virtual ALSA device: alias resolution, teaching writes the alias, unmapped alias receives nothing. The settings
panel renders; its Use Alias dialog and the Player override are untested by hand. Manual test set:
[`.tests-manual/midi-device-aliases.md`](../../.tests-manual/midi-device-aliases.md). Prefix decided: `TiXL:`.
**Relation to other plans:** Supersedes Phase 1 ("Default MIDI controller") of
[Plan_MidiControllerAbstraction.md](Plan_MidiControllerAbstraction.md). Phases 2–3 of that plan (roles,
per-parameter binding) build on top of aliases: a controller profile binds to an alias instead of a product name.

## Problem

Projects break whenever a MIDI device name changes: a different unit of the same controller, a different OS
(WinMM, ALSA and CoreMIDI report different strings), a USB hub that renumbers ports, or simply a different
controller at the venue. Every `MidiInput` / `Midi*Output` op stores the physical product name verbatim, so a
rename means touching dozens of ops in dozens of symbols.

Goal: the device name stored in a project is a stable, user-chosen **alias**; the mapping from alias to the
physical device present on *this* machine lives in machine-local settings.

## Design

### Alias format

- An alias is an ordinary device-name string with a reserved prefix: `TiXL:ApcMini`, `TiXL:Keyboard`.
  No new input, no `.t3` format change; teaching, recordings, replay, the Player and user-written ops keep
  working with strings.
- One constant and two helpers in `IoServices/Midi/MidiDeviceAlias.cs`:
  `Prefix = "TiXL:"`, `IsAlias(string)`, `TryStripPrefix(string, out string)`.
- Empty device name keeps meaning "any device" (`MidiInput`) — unchanged.

### Mapping table — its own file, `midiAliases.json`

`MidiDeviceAliases` (IoServices) holds `List<MidiDeviceAlias>` (`Alias`, `ProductName`) as a plain JSON list.
The editor loads it from the settings folder at startup, the Player from its own folder, and the Player
exporter copies the editor's file next to the executable. One file name and format everywhere; copying the
file between machines moves a mapping. No Core changes. List, not dictionary, because the user orders it and
order carries meaning (see teaching).

Rules:
- Alias is unique within the list (dictionary semantics on the alias side).
- Several aliases may map to the same physical device (one controller standing in for two at a gig).
- An entry whose device is not connected is **kept**, shown as `(NOT FOUND)`; never auto-removed.
- `ProductName` empty = "Not Set".
- An existing file that fails to parse is never overwritten (same guard as `Settings<T>`).

### Resolution — on change only, never per MIDI message

`MidiConnectionManager.ResolveDeviceName(string name) : string`
- returns `name` unchanged if it is not an alias;
- returns the mapped product name if the alias is set;
- returns a sentinel that matches no device (e.g. the alias itself) if unmapped — **an unmapped alias
  matches nothing**. Falling back to "any device" would reintroduce the surprises this feature removes.

Each op keeps a `MidiDeviceNameCache` (IoServices) holding the requested name, the resolved product name,
the connection state and the status text. It redoes that work only when the requested name or
`MidiConnectionManager.DevicesVersion` changed; the version bumps on alias reload and on every device rescan.
The per-frame path is one int and one string compare, and the status text is no longer rebuilt per frame.

Consumers (all go through the one helper):
| Consumer | Where the name is read today | Change |
| --- | --- | --- |
| `MidiInput` | `Update`: `_trainedDeviceName = Device.GetValue(context)`; compared per message in `ProcessParsedMidiEvent` | resolve at the `Update` read, store the resolved name; per-message compare unchanged |
| `MidiOutput`, `MidiControlOutput`, `MidiNoteOutput`, `MidiPitchbendOutput`, `MidiTriggerOutput`, `MidiSysexOutput` | `Update`: `Device.GetValue(context)` then loop over `MidiOutsWithDevices` | resolve at the read |
| `IoDataSetRecorder` / `SimulatedIoBus` | record and replay physical product names | **no change** — matching happens after resolution in `MidiInput` |
| `CompatibleMidiDeviceHandling` | `[MidiDeviceProduct("APC MINI")]` vs product name | no change now; later profiles may bind to an alias |

### Status messages

`MidiInput` already implements `IStatusProvider`; the output ops already set `_lastErrorMessage`. Extend:
- alias unmapped: `Alias 'TiXL:ApcMini' is not mapped on this machine. Set it in Settings » Midi.`
- alias mapped but device not connected: existing "not captured" message, naming both alias and device.

### Teaching writes the alias

When `MidiInput` teaching captures a message, reverse-lookup the product name in the alias table. If one or
more aliases map to it, write the **first in list order** (the user's drag-and-drop order decides). Only if
none do, write the physical name as today. Without this every newly taught op is fragile again and the
"device-specific names" list never empties.

### Mark the input, do not hardcode the op list

Add `StringInputUi.UsageType.MidiDevice` and set it on the `Device` input of the seven Lib ops (`.t3ui`).
This gives three things at once:
- the usage scan finds every MIDI device input generically, including user-written ops;
- `MidiInput` gets the device dropdown it currently lacks (`MidiOutput` has one via `ICustomDropdownHolder`).
  Render it from `StringInputUi` for `UsageType.MidiDevice`: aliases first (from settings and from loaded
  symbols), separator, connected physical devices;
- the output ops can drop their individual `ICustomDropdownHolder` implementations later (optional).

### Player

`MidiAliasOverride.Apply` loads `midiAliases.json` from the Player's folder, applies `--midi-alias Alias=Device`
pairs from `PlayerStartupOptions.CommandLineArgs` on top, and logs the effective table so a broken show can be
diagnosed from the log.

## Settings » Midi UI

Everything below lives in `SettingsWindow.cs` `Categories.Midi` (extract to `MidiSettingsPanel.cs` in
`Editor/Gui/Windows/Settings/` if it exceeds ~150 lines; the window file is already long).

### Alias table

- Reorderable list with grip handle, add and remove buttons — reuse the drag logic of
  `FormInputs.AddFolderList`; generalise it to a row-callback variant rather than copying the drag code.
- Row = `TiXL:` fixed label + editable alias name | combo of connected devices (`Not Set` first, then
  devices; a stale name is shown as `<name> (NOT FOUND)` and stays selectable) | usage count.
- The table shows the **union** of saved entries and aliases referenced by loaded symbols. Referenced but
  unsaved aliases appear as `Not Set` rows; choosing a device saves them. Rows with no usage and no mapping
  can be removed; rows that are in use show the remove button disabled with a tooltip.
- Hovering the usage count shows the referencing children as `Project / Symbol / ChildName` paths
  (same data as the list below).
- Any change → `CoreSettings.Save()` + `MidiConnectionManager.ReloadAliases()`.

### Device-specific names

Header: "Device-specific names (loaded projects)". Help text explains fragility and points at the alias table.

Each row: `APC MINI  · used 32×  · 3 read-only` | **Use Alias...** button.
- Tooltip: referencing children as paths; read-only package entries marked.
- Button disabled with tooltip when *all* usages are in read-only packages or connected inputs.

**Use Alias... popup**
- Alias name input (prefix shown fixed), default derived mechanically: PascalCase the alphanumerics of the
  product name and drop version tokens (`MK2`, `mk II`, `v2`) → `APC MINI MK2` → `ApcMini`. No curated table.
- Device combo, preselected to the physical device the row is named after, if connected.
- Text: "Replaces the device name in N inputs across M symbols in K projects. Symbols are saved. R usages in
  read-only packages and C connected inputs are skipped."
- Confirm → one `MacroCommand` of `ChangeInputValueCommand`s (undoable as a single step), add/update the
  alias entry, `ReloadAliases()`, rescan usage.

### Usage scan

`Editor/UiModel/Helpers/MidiDeviceUsage.cs` (editor-only): iterate `SymbolRegistry.SymbolsFromAllPackages()`
→ `Children` → inputs whose `InputUi` is `StringInputUi` with `UsageType.MidiDevice` (same loop shape as
`SymbolAnalysis.LogInvalidAssetReference`). Collect per device string: list of (symbolId, childId, inputId,
isReadOnly, isConnected). Cached; recomputed when the Midi settings page is opened, after Rescan, after a
Use Alias rewrite, and via an explicit refresh button. **Never per frame.**

## Phases

### Phase 1 — Resolver and settings field (Core / IoServices / Operators)
- `MidiDeviceAlias` helpers, `CoreSettings.MidiDeviceAliases`, `MidiConnectionManager.ResolveDeviceName` /
  `ReloadAliases`.
- Seven Lib ops resolve at the `Update` read; `MidiInput` teaching writes the alias; status messages.
- `StringInputUi.UsageType.MidiDevice`, set in the seven `.t3ui` files (editor closed while editing).
- Verify: hand-edit `projectSettings.json`, alias a device, confirm `MidiInput` matches and recordings replay.

### Phase 2 — Settings UI
- Reorderable alias table (generalised `AddFolderList` row drag), union with referenced aliases, `(NOT FOUND)`.
- `MidiDeviceUsage` scan + device-specific names list with tooltips.
- `MidiInput` device dropdown via `UsageType.MidiDevice`.

### Phase 3 — Use Alias rewrite
- Popup, default-name derivation, `MacroCommand` rewrite, read-only / connected skipping, save.

### Phase 4 — Player
- `midiAliases.json` copied by the exporter, `--midi-alias` pairs, startup log of the effective table.
  "Export alias file…" button in the settings panel.

### Docs and tests
- `.help/docs/using/MidiDeviceAliases.md` (what an alias is, where the table lives, Player file).
- `.tests-manual/midi-device-aliases.md`: map alias → device; teach an op and confirm it stores the alias;
  unplug the device and confirm `(NOT FOUND)` persists; unmapped alias shows the status warning and receives
  nothing; Use Alias rewrites N inputs and is one undo step; read-only usages are skipped and counted;
  recorded IO replays into an aliased `MidiInput`; Player picks up `midiAliases.json`.
- Update `Plan_MidiControllerAbstraction.md`: Phase 1 superseded by this plan.

## Open questions

1. Prefix spelling: `TiXL:` is readable but a colon inside a device name may look like a port separator to
   users coming from ALSA (`client:port`). Alternatives: `@ApcMini`, `[ApcMini]`. Decide before the first
   project saves an alias — the prefix is forever.
2. Should `CompatibleMidiDeviceHandling` (snapshot controllers) prefer a device that is mapped to an alias when
   several known controllers are connected? Cheap to add; defines "the main controller" without a new setting.
3. Dropdown for `Device` on the output ops: keep their `ICustomDropdownHolder` or move all to the
   `UsageType.MidiDevice` rendering in one go.
4. Resolved: the Player reads only the file next to the exe; a show depends on what sits in its own folder.
