# Debug Protocol — Phase 0 Audit

Audited 2026-09-02. Condensed from three codebase surveys; feeds Phases 1-6.

## Addressable entities

| Entity | Address | Resolver |
|---|---|---|
| Symbol | `Symbol.Id` (Guid, global) | `SymbolRegistry.TryGetSymbol`, `SymbolUiRegistry.TryGetSymbolUi` |
| Op in composition | `Symbol.Child.Id` (Guid, unique per parent) | `symbol.Children[id]`, `Structure.TryGetUiAndInstanceInComposition` |
| Live instance | `Instance.InstancePath` = root-first `Guid[]` | `Structure.GetInstanceFromIdPath` (per `ProjectView`) |
| Input/output slot | definition Guid within child | `child.Inputs[id]` / `Outputs[id]` |
| Connection | 4 Guids + `multiInputIndex` (`Guid.Empty` side = composition's own IO) | `Symbol.Connections` order disambiguates multi-input |
| Project | `EditorSymbolPackage.DisplayName` or home `Symbol.Id` | `SymbolPackage.AllPackages` |

Wire formats: symbol `"<guid>"`; op `"<compositionSymbolId>/<childId>"`; instance `["guid",...]`; slot `{instance, slotId}`.

Ambient handles: `ProjectView.Focused`, `OpenedProject.OpenedProjects`.

## Commands: dispatch feasibility

~46 `ICommand` classes, all in `Editor/UiModel/Commands/`. **All four high-value
paths are plain-data constructible** (class A/C — Guids/values, registry-resolved):

- Add op: `AddSymbolChildCommand(symbol, symbolIdToAdd)` (+`PosOnCanvas`; read `AddedChildId`)
- Connect: `AddConnectionCommand(symbol, Connection(4 Guids), multiInputIndex)`; cycle check via `Structure.CheckForCycle`
- Set param: `ChangeInputValueCommand(symbol, childId, input, newValue)` — JSON bridge exists: `InputValue.SetValueFromJson(JToken)`. Ctor reads `Playback.Current` -> construct on main thread only
- Delete: `DeleteSymbolChildrenCommand(symbolUi, childUis)` (snapshots to Guids at ctor)
- Wrap multi-step dispatches in `MacroCommand`

Not protocol-reachable without adapters (fine — out of subset scope): animation-curve
commands (live `Curve`/`Animator`), all Variation commands (`SymbolVariationPool`),
`ModifyCanvasElementsCommand` (`ISelection`), `ChangeSymbolNamespaceCommand`.

`UndoRedoStack` (`Editor/UiModel/Commands/UndoRedoStack.cs`): static, no locks —
implicitly main-thread-only. Confirms the enqueue-on-socket/execute-on-main design.

## UserActions

`UserActions` enum (~150) in `Editor/Gui/Interaction/UserAction.cs`; **polled**, not
dispatched — `action.Triggered()` requires physical key state + focus/hover context
(`KeyBinding.IsContextValid`), i.e. the exact gating that broke synthetic input.
`UserActionRegistry.QueueAction` exists but only 3 timeline actions consume it.
Decision: **graph mutations dispatch commands directly**; UserActions only for
playback/UI toggles later, via extending `Triggered()` to consume the queue.

## Mutation bypasses (getGraphState will see what the command log can't explain)

Chokepoint reality: `Symbol.AddConnection/RemoveConnection/RemoveChild` are public
and mutate live instances — commands are convention, not enforcement. Top bypasses:

- `Duplicate.cs` / `Combine.cs`: direct `AddConnection` + **`UndoRedoStack.Clear()`**
- `InputValueUi.cs:987`: slot type change + `Clear()`
- `DeleteSymbolDialog.Helpers.cs`: cascade delete across depending symbols, no command
- Legacy graph: `ConnectionMaker.cs:945` direct add; `GraphNode.cs:1117` raw
  `Connections.Remove` during draw
- `RecordingSession.cs`: per-frame TimeClip growth while recording (version churn)
- Various draw-code `Input.IsDefault=false` writes (list inputs, custom op UIs)

See `Plan_UndoRedoCoverage.md` for the historical inventory. Not blocking the
protocol; documented so state observations are explainable.

## Runtime infrastructure map

| Need | Finding |
|---|---|
| Main-thread pump | Top of `T3Ui.ProcessFrame()` (`T3Ui.Update.cs:31`) — post-`NewFrame` (input latched), pre-draw; same-frame read-your-writes. Deferred-flag precedent: `UpdateModifiedProjects` |
| Args | No parser; copy `Program.ApplyVersionIdOverrideArg` pattern (`=` and space forms) |
| Log ring buffer | New `ILogWriter` (~40 lines); template `ConsoleLogWindow`; **seq numbers are new**, assigned in `ProcessEntry`; register in `Program.Main` with the other writers. `ProcessEntry` runs on the logging thread |
| Screenshot | `ScreenshotWriter.StartSavingToFile(texture, path, format, onComplete)` — async (one readback per playback frame); output texture via `RenderProcess.MainOutputTexture` (needs open Output window); whole-window via `ProgramWindows.CopyUiContentToShareTexture` wiring |
| Reload | `EditableSymbolProject.TryRecompile` is private. Low-risk: set `CodeExternallyModified=true` + let `UpdateModifiedProjects` run (0.5s debounce). Sync errors: widen to internal. `failureLog` is one raw MSBuild string. Recompile blocks the frame loop for seconds — respond async |
| Frame/metrics | `io.DeltaTime` (authoritative), `ImGui.GetFrameCount()` + `Playback.FrameCount` (report both), `RenderStatsCollector.ResultsForLastFrame` (name->count map, already populated). `PerformanceMetrics.RecordFrame` only runs while the menu bar draws — don't depend on it |
| structureVersion | **Missing globally** — added: `SymbolUi.GlobalVersionCounter`, bumped in `BumpVersionCounter()` (the funnel all edits pass). Symbol-set changes: `EditorSymbolPackage.SymbolStructureVersionCounter` |
| GPU memory | **Missing** — needs `IDXGIAdapter3.QueryVideoMemoryInfo` (SharpDX `Adapter3`); today only total VRAM is read |
| Resource counts | **Missing** — `ResourceManager` has no counters; cheapest: `SrvManager` count property |
| JSON | Newtonsoft everywhere; POCO + `JsonConvert` / `JObject.Parse` idiom |
| TCP template | `Operators/Lib/Symbols/io/tcp/TcpServer.cs` (TcpListener + CTS + accept loop) |
| Open project/composition | Copy `SkillTraining.cs:99-131`: `OpenedProject.TryCreate(WithExplicitHome)` -> `GraphWindow.TrySetToProject` -> `ProjectView.TrySetCompositionOp(idPath)` |
| Playback control | `Playback.Current.TimeInBars/.TimeInSecs` set; playing = `PlaybackSpeed = 1/0`; must apply before `PlaybackUtils.UpdatePlaybackAndSyncing()` in the frame |
| Selection | `NodeSelection.TrySelectCompositionChild(comp, id, add)` / getters; per `ProjectView`, internal |
| Shutdown | `Program.IsShuttingDown`; app exit via closing the main form (`Application.Exit()`) |

## Decisions taken from this audit

1. Socket threads parse nothing: raw lines are enqueued; the main-thread pump parses,
   executes, responds — uniform error handling, `ImGui.GetFrameCount()` valid.
2. Envelope carries `frame` (ImGui), `playbackFrame`, `structureVersion`
   (`SymbolUi.GlobalVersionCounter`).
3. Dispatch targets commands directly, never UserActions, for mutations.
4. Reload responses are asynchronous (recompile stalls the loop).
5. Instance addressing uses root-first Guid paths end-to-end.
