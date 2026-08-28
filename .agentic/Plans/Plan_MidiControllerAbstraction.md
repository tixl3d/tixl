# MIDI Controller Abstraction

**Status:** Draft — 2026-08-23. Design discussed, nothing implemented. Editor-only scope for now.

VJ setups currently hard-code specific MIDI devices (APC mini / APC mini MKII / APC 40) both in `MidiInput` ops
and in the editor's snapshot controller layer. Goal: a device-independent layer — "8 faders + 1 master +
optional scene grid" — that (a) lets a setup run on whatever controller is plugged in, and (b) lets the user
bind controller elements directly to parameters, similar to "enabled for snapshots".

## Goals (from the request)

1. Default MIDI controller, machine-local, analogous to the default audio input device
   (`CoreSettings.LocalAudioInputDeviceName`).
2. Device abstraction based on the APC mini mental model: 8 control faders/knobs, 1 master fader (snapshot
   blending), scene grid (snapshot switching). Support APC mini / mk2 / APC40, Launch Control XL, Midi Fighter
   and similar; later page switching.
3. Per-parameter controller binding, stored like the snapshot flag, with per-type value mapping:
   - float/int: min/max range + optional gain/bias curve; relative encoders supported
   - colour: gradient over the fader range
   - vector: value table with linear blending (curve-like); later a curve/edit canvas
4. Minimum contract a controller must provide: 8 main knobs or faders, 1 master; scene buttons optional.
5. Later: compact live visualization of the active controls (names, values) — alternative view of the
   parameter/control window, or a separate window usable in Focus Mode.
6. Extensible to other input means (OSC, SpaceMouse) later.

## Decisions taken in the discussion

- **Binding is per parameter** (per `Symbol.Child.Input`), like `IsInputEnabledForSnapshots`. Not per controller.
- **Binding scope = evaluation, not focus.** A binding is *live* while an instance of its op is being evaluated
  (first output's `DirtyFlag.FramesSinceLastUpdate <= 1`, the same signal the graph uses to dim idle nodes).
  Rationale: typical VJ setups have a master composition (always evaluated: glow, master fades, scene switch)
  and many exclusive scene sub-ops, each with its own snapshots and its own fader assignments. With a
  focus-based rule those scene bindings would never be live in performance; with the evaluation rule the
  active scene's faders are live automatically, and focusing a scene in the editor (it renders in the output
  window) makes its bindings live for building. Snapshot *activation* via the grid keeps the existing
  focused-composition rule of `VariationHandling.Update()` for now — separate concern, may follow later.
  No "always live" override: a composition that must react without being on the main output is expected to be
  evaluated through another output binding (e.g. future NDI/Spout outputs), which makes it live by the same rule.
- **Snapshots and controllers both simply write; last one wins.** No offsets, no priority — relative/additive
  schemes get confusing fast.
- **Role conflicts are shown, not resolved.** Master composition and an active scene bound to the same role
  both write (last wins). The live controller view lists every live binding per role plus `MidiInput` ops on the
  same control, so "what fader does what" is answerable at a glance; non-live bindings of inactive scenes are
  listed dimmed and an offline "all bindings in project" list groups them by symbol.
- **No hiding of controls from `MidiInput` ops.** Everything passes through; multiple `MidiInput` ops on the same
  CC stays legal and useful. Overlaps between a role binding and a `MidiInput` op on the same control are
  *shown*, not blocked. (Today a claimed `CompatibleMidiDevice` flips the whole device into control mode and
  starves `MidiInput` ops — this must be revisited, see Phase 2.)
- **Editor only for now.** Player support is wanted eventually but has large implications (the role layer
  would have to move out of `Editor`). Keep the new types in `Editor/` but avoid Editor-only dependencies
  (ImGui, `ProjectView`) in the *model* classes so a later move to `IoServices`/`Core` is mechanical.
- Two control methods, not three: "specific device name" and "default device" are the same mechanism with a
  name substitution; "mapped TiXL controller" (roles + bindings) is the genuinely different one.
- Identity stays the NAudio `ProductName` string (matches audio). Two identical controllers are
  indistinguishable; accepted for v1.

## Current state (audit summary)

| Area | Where | Today |
| --- | --- | --- |
| Device registry | `IoServices/MidiConnectionManager.cs` | Opens all devices, identity = `ProductName`, `IMidiConsumer` fan-out, per-device control mode (`SetDeviceControlMode`), `CoreSettings.LimitMidiDeviceCapture` filter. |
| Graph op | `Operators/Io/lib/io/midi/MidiInput.cs` | `Device` string compared verbatim to product name (empty = any). Teaching writes product name. **No device dropdown** (`MidiOutput` has one via `ICustomDropdownHolder`). Absolute 0..127 only, no relative/encoder mode. |
| Op node UI | `Editor/Gui/OpUis/UIs/MidiInputUi.cs` | Shows control/channel/device, activity flash. |
| Controller layer | `Editor/Gui/Interaction/Midi/CompatibleMidiDevice.cs`, `CompatibleMidiDeviceHandling.cs`, `CommandProcessing/*`, `ControllerGridLayout.cs` | Reflection-discovered per-product classes (`[MidiDeviceProduct("APC MINI")]`), `ButtonRange` → canonical 8x8 index, `ModeButton`/`InputModes`, `CommandTriggerCombination`, LED feedback (`UpdateRangeLeds`, `SendColor`). |
| Device classes | `CompatibleDevices/` | ApcMini, ApcMiniMk2, Apc40Mk1, Apc40Mk2, LaunchpadMiniMk3, NanoControl8 (XTouchMini stub). All bind **snapshot actions only**. |
| Faders hook | `BlendActions.UpdateBlendValues(int, float)` bound to `Sliders1To8`/`Fader1To8` | **Empty body.** `StartBlendingSnapshots` logs "not implemented". `ParamCollectionActions.SetParamGroupControl` referenced only in a commented-out line in `ApcMini.cs`. |
| Snapshot targets | `Editor/Gui/Interaction/Variations/VariationHandling.cs` | `ActivePoolForSnapshots` / `ActiveInstanceForSnapshots` resolved per frame. |
| Per-parameter flag | `Editor/UiModel/SymbolUi.Child.cs` | `EnabledForSnapshots`, `SnapshotEnabledInputIds`, `SnapshotGroupIndex` (>1 reserved for "parameter collections", unused). Undo: `ChangeSnapshotEnabledCommand`, `ChangeSnapshotEnabledInputsCommand`. Indicator colour `UiColors.StatusControlled`. |
| Control surface | `Editor/Gui/Windows/SnapshotControlView.cs`, `SnapshotControllerGrid.cs` | Per-parameter surface for snapshot-affected ops; 8x8 grid popup by activation index. |
| Default-device pattern | `Core/IO/CoreSettings.cs` `LocalAudioInputDeviceName`, `Core/Audio/WasapiAudioInput.ResolveInputDeviceName`, `Editor/Gui/Audio/AudioDeviceSelector.cs` | Empty project value → machine-local default; `"(NOT FOUND)"` status in settings UIs. |
| Op→editor requests | `Core/IO/SnapShotBlendingData.cs`, ops `ActivateSnapshot`, `BlendSnapshots` | Request object + status write-back; precedent for op-driven control. |
| Value mapping helpers | `Core/Utils/MathUtils.cs` (`Remap`, `ApplyGainAndBias`, `DampTowards`), `Core/DataTypes/Gradient.cs` (`Sample`), `Editor/Gui/UiHelpers/GradientEditor.cs`, `Core/Animation/Curve` | All exist. |
| Record/replay | `IoServices/SimulatedIoBus.cs`, `MidiDataRecording.cs` | Replays MIDI by product name. |
| Other inputs | `IoServices/OscConnectionManager.cs` + `OscInput`; `Editor/Gui/Interaction/Camera/SpaceMouse*.cs` (camera-only) | Parallel sources for later. |
| Settings UI | `Editor/Gui/Windows/SettingsWindow.cs` Midi category | Rescan, `LimitMidiDeviceCapture`, debug logging. |

## Architecture

### A1 — Default MIDI controller = name substitution (Phase 1)

`CoreSettings.ConfigData.LocalMidiControllerName` (machine-local, `projectSettings.json`, next to
`LocalAudioInputDeviceName`; not `UserSettings` because the Player will eventually need it). A reserved value in
`MidiInput.Device` (empty string currently means "any device" and must keep meaning that; use a sentinel such as
`"Default"` exposed as a dropdown entry "Default MIDI Controller") resolves through one static helper
`MidiConnectionManager.ResolveDeviceName(string)` — same shape as `WasapiAudioInput.ResolveInputDeviceName`.

### A2 — Roles instead of product-specific ranges (Phase 2)

Split the current `CompatibleMidiDevice` (device + snapshot bindings in one class) into:

- **`ControllerProfile`** (per product): declares which physical controls exist and maps raw MIDI to
  **`ControllerRole`** elements. Keep the existing `ButtonRange`/canonical-grid machinery; add a control
  descriptor with `IsRelative` (encoders), and LED capability flags.
- **Role set** (the contract from goal 4): `Fader1..8`, `Master`, `Grid(row,col)`, `Shift`, optional
  `Encoder1..N`, `Button1..8`. Profiles leave roles they cannot provide unbound (NanoControl8: no grid;
  Midi Fighter: no faders).
- Snapshot actions rebind to roles (`Grid` → activate, `Master` → blend progress, …) — behaviour unchanged.
- Name the abstraction `ControllerRole`, not `MidiRole`, so an OSC or SpaceMouse profile can feed the same
  roles later.
- Relative encoders accumulate into a normalized 0..1 value with a sensitivity; absolute faders get optional
  soft-takeover (pickup) to avoid value jumps after page/composition switches.

### A3 — Per-parameter binding (Phase 3)

`ControllerBinding { ControllerRole Role; Mapping }` stored per input, sibling of `SnapshotEnabledInputIds`.
**Do not add another loose field to `SymbolUi.Child`** — fold the existing snapshot flags and the new binding
into one small per-child "control state" record (the reserved `SnapshotGroupIndex > 1` semantic suggests this
was intended). Serialized with the `.t3ui`, same place as snapshot flags; undoable via new commands modelled on
`ChangeSnapshotEnabledInputsCommand` (store Guids, resolve per call).

Mapping per type:
- float/int: `Vector2 Range`, `Vector2 GainBias` (reuse `MathUtils.ApplyGainAndBias`), int rounds.
- colour: `Gradient`, sampled with `Gradient.Sample(t)`; edited with `GradientEditor`.
- vector (2/3/4): start with **A/B blend** (two stored values, lerp by t) — covers the common VJ cases without
  new UI; the value table / curve canvas is a later extension with the same `t → value` contract.
- bool: threshold.

Resolution each frame: iterate a cached list of all (symbolId, childId, inputId) bindings across loaded
symbols (invalidated on graph/binding change — no per-frame LINQ); a binding is live if any instance of that
child has `FramesSinceLastUpdate <= 1` on its first output (fallback for output-less ops: the parent composition
instance). Apply the latest role value via `SetTypedInputValue` on the symbol child input — same write target as
snapshots, last writer wins. Hundreds of bindings at most; cost is negligible.

### A4 — Visibility of overlaps (Phase 2/3)

Replace all-or-nothing device control mode: the profile does not starve `MidiInput` ops. Instead provide a
query "which roles / raw controls are currently bound to what" so the settings Midi panel and the `MidiInput`
node UI can show "also bound to Fader3 → Blur.Strength". Keep `SetDeviceControlMode` only if a profile needs
exclusive access for LED handshakes (Launchpad programmer mode) — document that case.

## Phases

### Phase 1 — Default MIDI controller (small, ship first)
- `CoreSettings.LocalMidiControllerName` + `MidiConnectionManager.ResolveDeviceName`.
- `MidiInput`: implement `ICustomDropdownHolder` for `Device` (copy `MidiOutput`), with "Default MIDI Controller"
  entry; resolve at match time; `IStatusProvider` warning when the resolved device is not connected.
- Settings Midi panel: "Default MIDI controller" combo (copy `AudioDeviceSelector.DrawLocalDefaultDeviceCombo`),
  `(NOT FOUND)` state.
- Teaching keeps writing the concrete product name; context menu / checkbox "Use default controller" swaps it.
- `CompatibleMidiDeviceHandling`: when several profiles are connected, the default controller wins for snapshot
  control (today: all matching devices are instantiated).
- Manual test: `.tests-manual/midi-default-controller.md`.

### Phase 2 — Roles
- Introduce `ControllerRole`, `ControllerProfile`; migrate the six device classes (keep their ButtonRanges, add
  the role map). Rebind `SnapshotActions`/`BlendActions` through roles. Remove the empty
  `UpdateBlendValues`/`StartBlendingSnapshots` stubs or implement them via the binding layer.
- Encoder/relative support + soft-takeover.
- Add Launch Control XL and Midi Fighter Twister profiles (verify CC maps against hardware; Launch Control XL
  has 24 knobs + 8 faders, Twister has 16 relative encoders).
- Overlap query + display in settings Midi panel.

### Phase 3 — Parameter binding
- Control-state record on `SymbolUi.Child` (fold snapshot flags in), serialization, undo commands.
- Binding UI: parameter context menu "Bind to Controller…" with learn (touch a control), role dropdown,
  mapping editor per type (range+gain/bias, gradient, A/B vectors). Indicator uses `UiColors.StatusControlled`
  (green = controllable) on the parameter row and graph node, distinct glyph from the snapshot flag.
- Per-frame apply loop with cache; last-writer-wins with snapshots.
- Show bound roles in `SnapshotControlView` rows.
- **Live controller view (pulled forward from Phase 4):** strip with one column per role — role name, live
  binding(s) `Symbol / Parameter`, value bar, conflict marker when several live bindings or `MidiInput` ops
  share the control. Plus an "all bindings" list grouped by symbol (dimmed when not live).
- Manual test: master + two exclusive scenes behind a `Set`; bind Fader 1 in both scenes; switch scenes and verify
  only the active scene's binding is live; focus the inactive scene in the editor and verify it becomes live
  and the conflict is shown; verify snapshot interplay (last writer wins).

### Phase 4 — Polish (later)
- Controller view as an alternative view of the parameter/control window and usable in Focus Mode.
- LED feedback for bound roles on RGB grids / ring LEDs where the profile supports it.

### Later / out of scope for now
- Player support (requires moving A2/A3 model out of `Editor`).
- Pages (child compositions or group index), OSC / SpaceMouse profiles, value-table curve canvas for vectors,
  stable identity for duplicate controllers.

## Open questions

1. Sentinel for "default" in `MidiInput.Device`: literal `"Default"` vs. a separate bool input
   `UseDefaultController` (cleaner for teaching/overwrite semantics, one more input on a busy op).
2. Where exactly the control-state record lives — `SymbolUi.Child` nested type vs. a dedicated
   `ParameterControlState` keyed by child id inside `SymbolUi`. Decide when touching the snapshot flags.
3. Bool/enum bindings: threshold only, or button roles (`Button1..8`) toggling them?
4. Should a bound parameter also be implicitly "enabled for snapshots"? (Probably independent; a fader-controlled
   parameter that snapshots also write is exactly the last-writer-wins case.)
5. Liveness threshold: `FramesSinceLastUpdate <= 1` vs. a few frames of hysteresis so a scene that is
   cross-faded out doesn't flicker between live/not-live. Decide with hardware in hand.
