# Settings Refactoring

## Completed Work (previous sessions)

### Commit 1–6: Foundation
- Renamed global `ProjectSettings` → `CoreSettings` (keeps `projectSettings.json` filename)
- Renamed per-symbol `PlaybackSettings` → `ProjectSettings` (now `CompositionSettings`)
- Popup → proper Window, file format versioning, render export settings in `.t3ui`
- RenderWindow modified tracking, post-render auto-increment

### CoreSettings Cleanup
- Renamed `GlobalMute`/`GlobalPlaybackVolume` → `AppMute`/`AppVolume`
- Moved project-specific fields into per-symbol sub-classes (Audio, Export)
- Added `CompositionSettings.Current` static accessor
- Moved settings classes to `T3.Core.Settings` namespace (`Core/Settings/` directory)
- Moved `FileLocations` and `UserData` to `T3.Core.Settings` namespace

### RenderSettings Refactoring
- Removed `ForNextExport` intermediary — `RenderSettings.Current` reads/writes directly from `SymbolUi.RenderSettings`
- Fixed clone bug on composition switch (was sharing reference, now clones)
- Legacy migration preserved for backward compat (runs once per composition, persists immediately)
- Wired up defaults for FPS, Bitrate, MotionBlur, AutoIncrement, ExportAudio, FileFormat, ResolutionFactor
- Fixed `AddInt` reset button never highlighting (missing `isDefault` check)
- OutputWindow uses `RenderProcess.GetActiveOrRequestedSettings()` during export (not `Current`)

### Naming Cleanup
- `ProjectSettings` → `SymbolSettings` → `CompositionSettings` (class + all consumers)
- `Symbol.ProjectSettings` → `Symbol.CompositionSettings`
- IO and Performance configs moved back to `CoreSettings` (app-level, not per-composition)
- JSON key `"ProjectSettings"` preserved in `.t3` for backward compat

### Composition Settings Window
- Converted from popup to proper dockable `Window` (class `ProjectSettingsWindow`)
- Categories: Playback, Audio, Executable
- App menu shows "Composition Settings" with checkmark when focused op defines settings
- Timeline gear icon toggles window visibility
- Audio toggle in toolbar toggles `AppMute` (app-wide) instead of per-project SoundtrackMute

### Editor State Persistence

**OutputWindow State** (`OutputWindowState.cs`, stored on root op's `.t3ui`):
- Gizmo visibility/mode — direct backing store on `State` (no copy)
- Background color, resolution, camera position/target/roll/speed — copy-based, synced every frame via `SyncCopyFieldsToState()`
- Camera control mode (AutoUseFirstCam, SceneViewer, etc.)
- Pinning state (isPinned, instance path as Guid[], output ID)
- Multiple output windows supported (stored as JSON array)
- Saves on project close (`ProjectView.Close`), composition switch, and continuously via per-frame sync
- Uses `ProjectView.Focused.RootInstance` (project-global, not per-composition)
- Fixed: `ProjectView.Close` no longer calls `Unpin()` — saves state instead
- Fixed: Pinning survives project close/reopen (stale ProjectView re-resolved against current)
- Fixed: State saved to correct project on switch (tracks `_lastSyncedSymbolUi`)

**Timeline State** (`TimelineState.cs`, stored per-symbol in `.t3ui`):
- View state only: ScaleX, ScrollX, Mode (DopeView/CurveEditor)
- Saved/loaded on composition change via `SyncStateWithComposition`
- Loop range, playback position stay as runtime state on `Playback` object

**Window Layout** (stored on root op's `.t3ui`):
- `SymbolUi.WindowLayout` — ImGui INI layout string via `SaveIniSettingsToMemory()`
- `SymbolUi.WindowLayoutImGuiVersion` — version tag for compatibility
- `SymbolUi.WindowVisibility` — Dictionary<string, bool> of window title → visible
- Saved in `ProjectView.Close()`, restored in `ProjectView.SetAsFocused()`
- Version check: only restores if ImGui major.minor matches
- Opt-in via `UserSettings.Config.SaveWindowLayoutsWithProjects` (default: true)

---

## Architecture

### Three-Tier Settings Model

**Project-global** (on root op's `.t3ui` only):
- Output window state (resolution, camera, gizmos, pinning)
- Window layout + visibility
- IO (OSC port) — in `CoreSettings`
- Performance flags — in `CoreSettings`

**Per-symbol with CompositionSettings** (inherited via breadcrumb, in `.t3`):
- Playback config (BPM, soundtrack, audio source, syncing)
- Audio mix (volumes, mutes, resync threshold)
- Export config (window mode, keyboard control)
- Render settings (in `.t3ui`)
- Timeline view state (in `.t3ui`)

**App-level** (`CoreSettings` in `projectSettings.json`):
- AppMute, AppVolume
- LimitMidiDeviceCapture
- DefaultOscPort, TimeClipSuspending, SkipOptimization, EnableDirectXDebug, EnableBeatSyncProfiling
- Logging flags

### Current Structure
```
CompositionSettings (Core\Settings\CompositionSettings.cs, in .t3)
├── Enabled
├── Playback (PlaybackConfig)
│   ├── Bpm, AudioClips, AudioSource, Syncing
│   ├── AudioInputDeviceName, AudioGainFactor, AudioDecayFactor
│   └── EnableAudioBeatLocking, BeatLockAudioOffsetSec
├── Audio (AudioMixConfig)
│   ├── SoundtrackMute, SoundtrackVolume
│   ├── OperatorMute, OperatorVolume
│   └── AudioResyncThreshold
└── Export (ExportConfig)
    ├── DefaultWindowMode
    └── EnablePlaybackControlWithKeyboard

SymbolUi (Editor, in .t3ui under "Settings")
├── RenderSettings (RenderExport) — per-symbol
│   ├── FrameRate, StartInBars, EndInBars, TimeReference, TimeRange
│   ├── RenderMode, Bitrate, ResolutionFactor, OverrideMotionBlurSamples
│   ├── ExportAudio, AutoIncrementVersionNumber, CreateSubFolder, AutoIncrementSubFolder
│   ├── FileFormat
│   └── VideoFilePath, SequenceFilePath, SequenceFileName, SequencePrefix
├── TimelineState (Timeline) — per-symbol
│   ├── ScaleX, ScrollX
│   └── Mode (DopeView/CurveEditor)
├── OutputWindowStates (OutputWindows) — project-global (root op only), array
│   ├── ShowGizmos, TransformGizmoMode
│   ├── BackgroundColor
│   ├── CameraControlMode, CameraPosition, CameraTarget, CameraRoll, CameraSpeed
│   ├── ResolutionTitle, ResolutionWidth, ResolutionHeight, ResolutionUseAsAspectRatio
│   └── IsPinned, PinnedInstancePath, PinnedOutputId
├── WindowLayout — project-global (root op only), ImGui INI string
├── WindowLayoutImGuiVersion — version tag
└── WindowVisibility — project-global, Dictionary<string, bool>

CoreSettings (projectSettings.json)
├── AppMute, AppVolume
├── LimitMidiDeviceCapture
├── DefaultOscPort
├── TimeClipSuspending, SkipOptimization, EnableDirectXDebug, EnableBeatSyncProfiling
├── LogAssemblyVersionMismatches, LogCompilationDetails, LogAssemblyLoadingDetails, LogFileEvents
```

### Access Patterns
- `CompositionSettings.Current` — per-symbol composition config (null-safe, falls back to Defaults)
- `RenderSettings.Current` — reads directly from focused SymbolUi (lazy-inits with legacy migration)
- `OutputWindow.State` — reads from root op's SymbolUi via `_lastSyncedSymbolUi`
- `CoreSettings.Config` — app-level settings
- `UserSettings.Config` — user preferences

---

## Future Work

### Graph View State
- Canvas position and zoom per symbol (partially exists in `UserSettings.ViewedCanvasAreaForSymbolChildId`)
- Selected operator(s)

### Default Composition Settings
- `UserSettings.ConfigData` gets a `DefaultCompositionSettings` field
- New compositions initialize from user defaults
- Composition Settings Window uses defaults for reset-to-default buttons

### Automatic Visual Testing
- See `Plan_AutomaticTests.md`
- Load test project → restore editor state → capture screenshot → compare against reference
- Editor state persistence is a prerequisite for deterministic test captures

### Other
- Preferred resolution/aspect list per project
- Key-value parameter storage per project
- Pinned animation parameters (DopeSheetArea.PinnedParametersHashes)
