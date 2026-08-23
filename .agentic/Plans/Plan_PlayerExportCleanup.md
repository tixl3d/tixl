# Player & Export Cleanup

**Status:** In progress — 2026-08-23. Phases 0 (incl. prune), 1, 3, 4, 5 landed (uncommitted); Phase 2 remainder (background image, exe rename, ConfigData trim) open.
Covers the exported-executable pipeline (`Editor/UiModel/Exporting/PlayerExporter*.cs`) and the standalone
`Player/` app, which have grown hotfix-by-hotfix.

## Goals (from the request)

1. Startup dialog: display, resolution (native modes of that display), fullscreen (default on), show log
   messages (default off).
2. Dialog tech: WinForms is acceptable short-term; long-term it must run on macOS/Linux.
3. Single display output for now; multi-output is coming — don't paint ourselves into a corner.
4. Command-line parameters that set every option and skip the dialog.
5. Project settings get a preferred resolution.
6. Export strips ops that are neither connected nor auto-collected, with their resources and DLLs; ideally
   ships precompiled shaders.
7. Player: dark loading screen with progress bar right after startup, Esc cancels, last log line at the
   bottom, "next step" log phrasing, and a status report at the end of loading (op count, durations,
   shader/resource counts and sizes).
8. White-labeling in project settings: title (window title, ideally `Player.exe` rename) and a
   background/header image.

## Current state (audit summary)

Read these first; the plan references them throughout.

| Area | Where | What it does today |
| --- | --- | --- |
| Export entry | `Editor/Gui/AppMenuBar.cs:442` → `PlayerExporter.TryExportInstance` | No dialog; exports into `<package>/Export/<OpName>/`. |
| Symbol collection | `PlayerExporter.ExportInfo.cs` `ExportData.TryAddSymbol` | Recurses **all** `symbol.Children` statically — unconnected children drag their packages in. |
| Asset collection | `PlayerExporter.cs` `RecursivelyCollectExportData` | Walks input connections from the root `Texture2D` output; picks up only `StringInputUi` FilePath/DirectoryPath values. |
| Packages | `TryExportSymbolPackages` | Copies **whole** package build outputs (all `.t3`, all DLLs). Only prunes `.git`, `SymbolUis`, `SourceCode`, `Export`, `Assets`. |
| Optional native DLLs | `_dependencyDefinitions` table, `PlayerExporter.cs:534` | Hand-maintained GUID→filename list; applied only to the `Player/` directory copy. |
| Player runtime | `Editor.csproj` `CopyPlayer` ← `Player/bin/ReleasePublished/` (self-contained publish, ~300 DLLs) | Copied wholesale. `EditorResources/` copied wholesale too. |
| Settings channel | `Core/IO/CoreSettings.cs:48` `ExportSettings` record → `exportSettings.json` | Carries `WindowMode`, title, author (= package name, `// todo`), `BuildId`, editor version, and the **editor machine's** entire `CoreSettings.Config`. `EnablePlaybackControlWithKeyboard` travels via the `.t3` instead. |
| Project-side UI | `ProjectSettingsWindow.DrawRenderingSettings` (category "Executable") | Window mode + keyboard-control checkbox; backed by `CompositionSettings.ExportConfig`. |
| Player options | `Player/Program.cs` `Options` (+ an unused duplicate `Player/Options.cs`) | `--width/--height/--windowed/--loop/--logging/--novsync`; no display selection; `--windowed` is also string-matched by hand in `TryResolveOptions`. |
| Player window | `SharpDX.Windows.RenderForm`, borderless "fullscreen" on the form's current screen | No monitor choice; no loading screen; `PreloadShadersAndResources` hides the window and steps the timeline in 2 s increments. |
| Player dialogs | `BlockingWindow.Instance = new SilkWindowProvider()` | Silk.NET/GLFW + OpenGL ImGui windows — already shipped and used for message boxes. |
| Shader cache | `ShaderCompiler.Caching.cs` | `%AppData%/TiXL<ver>/Tmp/Cache/<subdir>/<hash>.shadercache`; Player subdir includes the per-export `BuildId` GUID. |
| Loading feedback | none in Player | Editor has `ISplashScreen : ILogWriter` (WinForms) — the log stream *is* the progress channel. |

### Findings worth calling out

- **Shader disk cache never hit across launches.** The key was `string.GetHashCode()` of source + entry
  point, which .NET randomizes per process. Verified: two runs of the same program hash the same literal to
  different values. Effect: every editor and player start recompiles every shader and writes a fresh set of
  files, so the cache only grows (1.8 GB / 36k files in `TiXL\Tmp\Cache` on the dev machine) and
  precompiled shaders could never be shipped. Fixed in Phase 0.
- **Audio init blocked for 2.5 s** in `AudioMixerManager.GetDefaultOutputSampleRate()`: it enumerated every
  WASAPI endpoint via `BassWasapi.GetDeviceInfo` (slow with disconnected / Bluetooth devices). Replaced by a
  throwaway `Bass.Init` + `Bass.GetInfo().SampleRate` (~80 ms). Affects editor start-up as well.
- **Source-based shader ops never used the cache** (`forceRecompile: true` in `IShaderOperator`); fixed.
- `Player/Options.cs` is dead code (the real `Options` is nested in `Program`).
- `Program.cs:117` builds the icon path as `Path.Combine(EditorResourcesDirectory, EditorResourcesDirectory, …)`
  (harmless because `Path.Combine` drops the first absolute segment, but wrong).
- `ExportData.TryAddSymbol` writes to `Console.WriteLine`.
- `"exportSettings.json"` is a string literal in both exporter and player.

## Architecture decisions (proposed)

### D1 — Startup dialog uses the existing SilkWindows ImGui window, not WinForms (decided)

`SilkWindowProvider.Show<T>(title, IImguiDrawer<T>)` already gives the Player a modal, themed ImGui window
on GLFW/OpenGL — the same stack that will carry us to macOS/Linux. The message box and the file manager
are written against it, so a `PlayerStartupDialog : IImguiDrawer<PlayerStartupOptions>` is ~150 lines with
no new dependency, and it is the long-term answer rather than a throwaway. Windows-only glue stays limited
to *applying* the result (positioning the DX11 `RenderForm` on the chosen monitor).

### D2 — Display enumeration goes through a small `SystemUi` abstraction

Silk.NET exposes monitors (`Window.GetWindowPlatform(false).GetMonitors()` → `IMonitor` with `Bounds`,
`Name`, `VideoMode`, `GetAllVideoModes()`), which is exactly "native resolution modes for the selected
display" and is cross-platform. Add to `SystemUi`:

```csharp
public readonly record struct DisplayInfo(int Index, string Name, Rectangle Bounds, bool IsPrimary, IReadOnlyList<Int2> Modes);
public interface IDisplayProvider { IReadOnlyList<DisplayInfo> GetDisplays(); }
```

implemented once in `SilkWindows` (GLFW) and used by the dialog. On Windows the chosen `DisplayInfo.Bounds`
is matched to `System.Windows.Forms.Screen` to place the `RenderForm`; the Editor's `IEditorSystemUiService.AllScreens`
can later be rebased on the same type. Displays are identified by **index + name** in options and CLI;
if the named display is gone at startup, fall back to primary and log a warning.

Multi-output readiness (goal 3): `PlayerStartupOptions` holds a `List<OutputTarget>` (display, resolution,
fullscreen) even though the dialog edits a single entry for now. Nothing else needs to change later except
the dialog and the render loop.

### D3 — One settings channel from project to player

All export-time configuration lives in `CompositionSettings.ExportConfig` (edited in the project-settings
window, saved in the exported op's `.t3`). The exporter copies it into `exportSettings.json` as the player's
defaults; the player never reads export config from the `.t3` directly. `ExportSettings` becomes:

```csharp
public sealed record ExportSettings(
    Guid OperatorId, Guid BuildId, string EditorVersion,
    string ApplicationTitle, string Author,
    ExportConfig Defaults,            // resolution, fullscreen, show logs, keyboard control, background image …
    CoreSettings.ConfigData ConfigData /* trimmed — see Phase 3 */);
public const string ExportSettingsFileName = "exportSettings.json";  // shared constant
```

Option precedence in the player: CLI > saved last-used (`<export>/.temp/playerSettings.json`)
> project defaults from `exportSettings.json`. The dialog is shown unless the project's
`ExportConfig.SkipStartupDialog` (default false) or `--no-dialog` is set; `--dialog` forces it back on
(e.g. to fix a bad saved choice) and `--reset` clears the saved last-used values.

### D4 — Export ships the *reachable* graph, not whole packages

"Reachable" = everything `RecursivelyCollectExportData` visits from the root output **plus** auto-collected
ops that evaluate without an output connection: `IAudioClipProvider`s with AutoPlay (`AudioClipCollector`),
loose audio-graph sources (`AudioGraphCollector`), and anything else the player's render loop collects
(keep this list in one place — `PlayerExporter.AutoCollectedTypes` — so new collectors extend it).

From the reachable instance set the exporter derives reachable **symbols**, and from those the files to ship:

- `.t3` files: only reachable symbols. For a type without a `.t3`, `SymbolPackage.LoadSymbols` creates an
  empty placeholder symbol (`SymbolPackage.cs:298`) — no children, no instances — so dropping unreachable
  `.t3` files is safe and shortens player startup (fewer JSON parses). Verify nothing in the player logs a
  warning per placeholder.
- Managed DLLs: per package, the package assembly plus the transitive closure of `GetReferencedAssemblies()`
  resolved against the package build output; unreferenced DLLs are dropped.
- Native / optional DLLs: replace `_dependencyDefinitions` with an attribute on the operator class,
  `[NativeDependencies("mediapipe_c.dll", "cvextern.dll")]` (Core), read via reflection from the already
  loaded types of reachable symbols. Unmatched native files in the package's `dependencies/` and in the
  `Player/` runtime copy are excluded. The existing GUID table becomes the migration seed for the
  attributes on `Lib` ops.
- Assets: unchanged mechanism, plus the background image from D5.

Not in scope: trimming the self-contained .NET runtime itself (`Player/bin/ReleasePublished`). Note the
option of a framework-dependent publish as a follow-up; it halves the export size but adds an install
prerequisite.

### D5 — White-label fields live in `ExportConfig`

`ExportConfig` gains `Title` (default: symbol name), `Author`, `BackgroundImage` (asset address, e.g.
`Proj:images/loading.png`; shown on the loading screen and as header in the startup dialog), and
`PreferredWidth/Height` (default 1920×1080), `DefaultWindowMode` (kept), `ShowLogs` (default false) and
`SkipStartupDialog` (default false). The exporter names the output folder and renames `Player.exe` → `<Title>.exe` (plus
`Player.dll`/`.runtimeconfig.json`/`.deps.json` siblings — the apphost looks for `<exe-name>.dll`, so all
four are renamed together; verify on a test export). Window title and log folder use `Title` too.

### D6 — Loading screen is drawn by the player's own DX11 swap chain

Order of operations in `Main`: parse options → dialog → create `RenderForm` + device on the chosen display
→ **start loading on a worker thread** → render loop shows the loading screen until loading completes →
switch to the project's render callback. The loading screen is a dark clear plus a progress bar (two
quads via the existing full-screen shader path) and a single small text line. Text needs a renderer: use
the existing bitmap-font path from `Lib` if it's reachable without instantiating ops; otherwise a tiny
Direct2D/DirectWrite overlay on the swap-chain surface (`SharpDX.Direct2D1` is already referenced by Core).
Decide during Phase 5; keep the loading-screen drawer behind one class so the choice is local.

Progress comes from a `PlayerLoadProgress` object the loader updates (`Stage`, `StepIndex/StepCount`,
`CurrentItem`) and an `ILogWriter` that keeps the last message for the bottom line. Esc on the loading
screen sets a cancellation token checked between load steps.

Loading status report (goal 7.5): collected into a `LoadReport` struct (packages, symbols, instances,
shaders compiled/loaded-from-cache, assets by type with byte sizes, per-stage durations) and written as one
multi-line `Log.Info` block plus `loadReport.json` next to the log.

### D7 — Precompiled shaders ship as a cache seed

With the stable key (Phase 0), the exporter can seed the player's cache: after collecting, it runs the same
timeline-stepping pre-evaluation the player does (`PreloadShadersAndResources` moves to Core as
`ShaderWarmup`), then copies every `.shadercache` file whose hash was touched during that pass into
`<export>/ShaderCache/`. The player points `ShaderCompiler` at a read-only **seed directory** consulted
before its writable cache. Hash inputs stay the same on both sides because the source text shipped in the
export is identical. Shader-bytecode is D3D-compiler output and not GPU-specific, so it's portable.

Cache hygiene (balances reuse vs. size): on startup, delete cache files not written/touched for 30 days
(touch `LastWriteTime` on a disk hit) — bounded to the app's own subdirectory, skipping files that fail to
delete (locks). The Player subdirectory drops `BuildId` (fresh per export, so it was useless as a cache key
and risked the 259-char path limit) and uses `<Author>/<Title>/<OperatorId>`.

## Phases

Each phase is independently shippable and has its own manual test set under `.tests-manual/`.

### Phase 0 — Stable shader-cache key (landed, uncommitted)

- `ShaderCompiler.ComputeStableHash` (FNV-1a over source, entry point, shader type) replaces the
  `string.GetHashCode()` pair. Build verified. Existing cache files become orphans; the Phase 1 startup
  prune will clear them.
- Follow-up in this phase: the 30-day prune + touch-on-hit, drop `BuildId` from the Player subdir.

### Phase 1 — Player options and startup dialog (landed, uncommitted)

Done: `PlayerStartupOptions` (+ `CommandLineArgs`, last-used persistence, display resolution),
`PlayerStartupDialog` on `SilkWindowProvider` with the editor's Inter fonts, `IDisplayProvider` in
`SystemUi` implemented by `SilkWindowProvider`, `SimpleWindowOptions.Position`, Player is a `WinExe` with
`AllocConsole` behind `--show-logs`, `ExportConfig` gained `PreferredWidth/Height`, `ShowLogs`,
`SkipStartupDialog`, `Title`, `Author` (panel + `.t3` serialization), `ExportSettings` record carries `Export` and the
shared `FileName`. Player logs and `playerSettings.json` live in `<export>/.temp/` (AppData fallback when read-only). Manual test set: `.tests-manual/player-startup-dialog.md`. Not yet verified end-to-end with a
fresh export (the dialog and dialog-less start were verified against an existing export with overlaid
binaries). Open: dialog header image (waits for Phase 2's `BackgroundImage`).


- Delete `Player/Options.cs`; introduce `PlayerStartupOptions` (display, resolution, fullscreen,
  showLogs, loop, vsync, noDialog, reset) + CLI parsing with CommandLineParser; remove the hand-rolled
  `--windowed` string check.
- `IDisplayProvider` (D2) in `SystemUi`, Silk implementation, `PlayerStartupDialog` (D1) with header
  image slot, display dropdown, resolution dropdown (modes of selected display, "Custom…" entry),
  fullscreen and show-logs checkboxes, Start / Quit.
- Last-used persistence (D3 precedence).
- Apply: place `RenderForm` on the chosen display; honor resolution in windowed mode; borderless fullscreen
  on that display; show-logs toggles the `ConsoleWriter`.
- Tests: `.tests-manual/player-startup-dialog.md`.

### Phase 2 — Project settings & export settings unification

- `ExportConfig` fields (D5), `ProjectSettingsWindow` "Executable" panel: title, author, preferred
  resolution (with presets), fullscreen, show logs, keyboard control, background image picker.
- `ExportSettings` record (D3), shared filename constant, exporter writes it; player reads defaults from
  it. Migration: old `exportSettings.json` without `Defaults` keeps working for one version (player falls
  back to `WindowMode`).
- Background image exported as an asset; `Player.exe` rename.
- Trim `ConfigData` baked into the export to what the player actually reads (audio device, OSC port, log
  flags) — audit `CoreSettings.Config` uses in Core at runtime.

### Phase 3 — Export stripping (D4) (landed, uncommitted)

Done: reachability pass + root-level auto-collected audio ops (`IAudioClipProvider`, `IAudioSource`);
`.t3` files of shipped symbols are rewritten with unreachable children/connections/animations removed
(`SymbolJson.TryWriteFilteredSymbolFile`, unit-tested) so unused ops are neither parsed nor instantiated;
`[ExportDependencies("file", "av*.dll")]` attribute (Core) replaces the GUID table — declared on the Lib webcam /
AbletonLink / SwiftCam ops, Io Artnet + Video2DPointScanner, Ndi, unsplash, Mediapipe ops; files declared by any
loaded op are shipped only when an exported op declares them (`DependencyFileFilter`), applied to package dirs
and the Player dir; `runtimes/<rid>` other than win-x64/win dropped; copy summary (files / MB shipped and
skipped) logged. `ExportConfig.StripUnusedOperators` (default on) is the escape hatch. Manual test set:
`.tests-manual/player-export-stripping.md`. Deliberately not done: pruning managed DLLs by reference closure
(low gain, high risk with reflection-loaded assemblies) and `EditorResources` pruning (1 MB of fonts/images the
dialog uses anyway).

Known remaining weight: `OpenCvSharpExtern.dll` (66 MB) and the FFmpeg DLLs are copied into *every* package
build output because `Core` references OpenCvSharp4.Windows — dedup across packages would need a shared
native folder resolved by `TixlAssemblyLoadContext`; and the self-contained .NET runtime itself.


- Reachability pass, auto-collected set, `.t3` pruning, managed-reference closure, `[NativeDependencies]`
  attribute + migration of the GUID table to `Lib` ops, `EditorResources` pruned to what the player loads
  (fonts/icons used by shared shaders? — audit `SharedResources.Initialize`).
- Export report logged (what was shipped / dropped and why) — reuses the `LoadReport` shape.
- Tests: export a small project and a `Lib`-heavy one, verify size drop and that both run.

### Phase 4 — Precompiled shader seed (D7) (landed, uncommitted)

Done, simpler than planned: no export-time warm-up pass. `ShaderCompiler` records which `IResourceConsumer`
each cached shader belongs to (`ConditionalWeakTable`), and the exporter writes the bytecode of every shader
owned by a collected instance (plus owner-less shared shaders) to `<export>/ShaderCache/`
(`ShaderCompiler.ExportCacheEntries`). The player sets `ShaderCacheSeedDirectory` to that folder (read-only,
consulted first) and keeps its writable cache in `<export>/.temp/ShaderCache/` (`ShaderCacheRootPath`).
Caveat: only shaders the editor actually compiled/loaded in the session are seeded — view the op once before
exporting. Also fixed on the way: source-based shader ops (`IShaderOperator`, i.e. [PixelShader]/[ComputeShader]
from code — the SDF shader graph) passed `forceRecompile: true` and never used the cache; a 5.5 s generated SDF
shader recompiled on every launch.

Phase 0 follow-up also landed: `ShaderCompiler.PruneCache(30 days)` at editor and player start, touch-on-hit.


- Move `PreloadShadersAndResources` to Core (`ShaderWarmup`), run it in the exporter, copy touched cache
  files, seed-directory lookup in `ShaderCompiler`, report shader hit/miss counts in `LoadReport`.

### Phase 5 — Loading screen, cancel, and status report (D6) (landed, uncommitted)

Done, with one deviation from D6: loading stays on the **main thread** and is split into steps
(`PumpLoadingScreen` between packages / instance creation / preload samples) instead of a worker thread — the
immediate context is used by the preload evaluation, so a worker would have needed a context lock, and ops
may assume the main thread. `LoadingScreen` draws with Direct2D/DirectWrite onto the swap-chain back buffer
(device now created with `BgraSupport`; releases its render target before `ResizeBuffers`). Esc sets
`_loadCancelled`, checked between steps. `LastLogLineWriter` (first line only) feeds the bottom line;
`ShaderCompiler` logs `Compiling X @entry...` before compiling and counts compiled vs. cached shaders;
`PlayerLoadReport` logs/saves the summary. Manual test set: `.tests-manual/player-loading-screen.md`.


- Worker-thread loading with `PlayerLoadProgress`, DX11 loading-screen drawer, last-log-line overlay,
  Esc cancel, "next step" log phrasing (`Loading package Lib…`, `Compiling 12/40 shaders…`), `LoadReport`.
- Tests: `.tests-manual/player-loading-screen.md`.

## Open questions

1. ~~D1: Silk or WinForms?~~ Decided: Silk.
2. D5 exe rename: fine with renaming the four apphost files, or keep `Player.exe` and only set the window
   title/icon? (Rename is cosmetic for end users but affects crash-log paths and Sentry grouping.)
3. D4: is "auto-collected" the full list (AudioClip AutoPlay, loose audio sources)? Any other collectors
   the player's render loop depends on (MIDI/OSC inputs without output connection, `SetVar`-style ops)?
4. D7: shader-cache seed — per-export size is typically a few MB; OK to always ship, or opt-in in the
   Executable settings?
5. Cache hygiene: 30 days / touch-on-hit acceptable, or prefer a size cap (e.g. 500 MB LRU)?
6. Framework-dependent publish as a later option to halve export size — worth a ticket?
