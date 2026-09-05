# Automatic Test Plan for TiXL

> **Update (2026-09-02):** The debug protocol (`tixl-debug-protocol-plan.md`) changed this
> plan's economics. The "no headless mode / ImGui coupling" blocker no longer applies to
> integration tests: they drive a *real* editor over TCP. First xUnit infrastructure landed as
> `Tests/TiXL.DebugClient` (typed protocol client) + `Tests/Editor.IntegrationTests`
> (fixture launches or attaches to an editor; the former Python acceptance/visual-suite
> scripts migrated to facts). This shaves the yak noted below (2026-04-22) — Phases 1-2
> (pure command/serialization tests, no editor needed) now have a home whenever prioritized.
> Phases 4-5 are partially superseded by the protocol approach.

## Analysis Summary

### Current State

**What exists:**

- **Visual regression tests** via the `VisualTest`/`ExecuteTests` operator system. These compare rendered operator output against reference PNGs with pixel-deviation thresholds. Covers content generation (shaders, particles, PBR, etc.) but requires the Editor UI to run -- no CLI or CI integration.
- **Command pattern** (`ICommand` + `UndoRedoStack`) covers ~80% of graph mutations: add/delete nodes, connections, move nodes, change values, copy/paste, annotations, keyframes, timeline clips. 33 command classes total, including `MacroCommand` for composite operations.
- **No traditional unit tests** (no xUnit/NUnit/MSTest projects in the solution).
- **No headless mode** in either Editor or Player -- both require DX11 + a window.
- **CI only builds** (`dotnet build`), does not run any tests.

**What doesn't exist:**

- No programmatic test runner for commands or graph operations
- No UI interaction replay/recording system
- No smoke test that can run without a GPU window
- No test coverage of the MagGraph interaction layer (state machine, snapping, drag flows)

### Architecture Observations

1. **Commands are the ideal seam for integration testing.** They encapsulate graph mutations with full Do/Undo, take GUIDs and values as inputs, and don't depend on ImGui state. A test harness that creates a Symbol, runs commands against it, and asserts model state would cover the most regression-prone code paths with zero UI dependency.

2. **The MagGraph state machine** (`GraphStates` + `StateMachine<T>`) orchestrates all interactive flows (drag, connect, snap, placeholder). States have `Enter`/`Update`/`Exit` callbacks and could theoretically be driven programmatically via `GraphUiContext`, but this requires a live ImGui frame -- hard to test headlessly.

3. **ImGui is tightly coupled to rendering.** There's no "headless ImGui" mode. Testing anything that calls `ImGui.IsItemHovered()` or `ImGui.GetIO().MousePos` requires a real render context. This makes UI-level testing fundamentally harder than model-level testing.

4. **The Player already has a render loop** and could be extended to run visual regression tests from CLI without the full Editor, since it loads operators and evaluates them to textures.

5. **Commands are not serializable** today, but their constructor arguments (GUIDs, positions, values) are simple enough to construct from test fixtures.

### Key Insight

The highest-value, lowest-effort path is **command-level integration tests** that exercise the Symbol/Instance model without any UI. This covers the exact operations that cause most regressions (add node, connect, delete, copy/paste, undo/redo) while being fast, deterministic, and CI-runnable.

---

## Proposed Steps (sorted by effort/impact)

### Phase 1: Command-Level Integration Tests (HIGH impact, LOW effort)

**What:** Create a standard xUnit test project that tests ICommand implementations against in-memory Symbol graphs.

**Why:** Commands are where 80% of graph-mutation bugs live. They're pure model operations -- no GPU, no ImGui, no window needed. A broken `DeleteSymbolChildrenCommand.Undo()` that corrupts multi-input indices is exactly the kind of regression this catches.

**Effort:** ~2-3 days

**Steps:**
1. Add `Editor.Tests.csproj` (xUnit) referencing `Core` and `Editor`
2. Create a `TestSymbolFactory` helper that builds minimal Symbol graphs (a composition with 2-3 children and connections) without loading from disk
3. Write tests for the critical commands:
   - `AddSymbolChildCommand` -- Do adds child, Undo removes it
   - `DeleteSymbolChildrenCommand` -- Do removes, Undo restores children + connections + multi-input indices
   - `AddConnectionCommand` / `DeleteConnectionCommand` -- Do/Undo preserves slot state
   - `CopySymbolChildrenCommand` -- paste creates correct children with remapped GUIDs
   - `ModifyCanvasElementsCommand` -- positions update correctly
   - `ChangeInputValueCommand` -- value changes, Undo restores original
   - `MacroCommand` -- composite undo reverses in correct order
4. Add `UndoRedoStack` round-trip tests: Do -> Undo -> Redo produces identical state
5. Wire into CI: add `dotnet test` step to `.github/workflows/pr.yml`

**Risks:** Some commands may have hidden dependencies on static registries (`SymbolUiRegistry`). May need to initialize a minimal registry in test setup.

> **Note (2026-04-22):** Considered spinning up `Editor.Tests.csproj` while working on `Plan_UndoRedoCoverage.md` Batch 1 so each closed gap could ship with a regression test. Deferred because the infra setup (xUnit project referencing `Editor.csproj` which is `OutputType=Exe`, wiring CI, building `TestSymbolFactory`) is a sizable yak relative to the one-liner command fixes it would guard. Undo/redo gaps are being closed without automated tests for now; revisit when Phase 1 of this plan is actually prioritized.

---

### Phase 2: Symbol Serialization Round-Trip Tests (HIGH impact, LOW effort)

**What:** Test that Symbol/SymbolUi JSON serialization survives round-trips without data loss.

**Why:** Corrupt `.t3`/`.t3ui` files are catastrophic and hard to diagnose. Serialization regressions are silent until a user reopens a project.

**Effort:** ~1 day

**Steps:**
1. In the same test project, add tests that:
   - Load a `.t3` file via `SymbolJson`
   - Serialize it back to JSON
   - Deserialize again and compare field-by-field
2. Include edge cases: empty graphs, deeply nested compositions, symbols with all slot types
3. Add a fixture `.t3` file that exercises every serializable field

---

### Phase 3: CI Smoke Build + Test Gate (MEDIUM impact, LOW effort)

**What:** Extend the existing CI pipeline to run the new tests and block PRs on failure.

**Effort:** ~0.5 days

**Steps:**
1. Add to `.github/workflows/pr.yml`:
   ```yaml
   - name: Run tests
     run: dotnet test --configuration Release --no-build --verbosity normal
   ```
2. Ensure test project builds in Release configuration
3. Add status check requirement on the GitHub repo

---

### Phase 4: Player-Based Visual Regression in CI (HIGH impact, LOW effort)

**What:** Minimal extension to Player so it can evaluate a test operator and write results to a file.

**Why:** The existing `VisualTest`/`ExecuteTests` operator infrastructure already works. It just needs a thin CLI wrapper -- not a new test runner or rendering pipeline.

**Effort:** ~1-2 days

**Approach:** A JSON spec file drives the test run:

```json
{
  "operatorId": "guid-of-ExecuteTests-instance",
  "project": "path/to/project",
  "resultFilePath": "test-results.json",
  "contextVariables": { "key": "value" }
}
```

**Steps:**
1. Add `--test <spec.json>` command-line arg to Player
2. In test mode: load project, instantiate the specified operator, pass context variables into the evaluation context's string variable dict, evaluate
3. Extend `ExecuteTests` with a `TestResultFilePath` string input. When set, write the result summary (pass/fail counts, failure details) to that file path in addition to the existing in-UI display
4. After evaluation completes, Player reads the result file, exits with code 0 on all-pass or code 1 on any failure
5. Reference PNGs already exist in `.Defaults/Tests/` -- no new infrastructure needed

**CI integration:** Add a step that runs `Player.exe --test spec.json` and checks exit code. Needs a GPU-capable runner (self-hosted Windows machine, or GitHub Actions Windows runner with basic GPU support).

---

### Phase 5: Editor UI Screenshot Regression Tests (HIGH impact, MEDIUM effort)

**What:** An in-Editor auto-test feature that starts the Editor, switches through layouts and editor states, captures the UI backbuffer as screenshots, and compares against references.

**Why:** Most regressions are visual -- broken layouts, missing windows, rendering glitches, misaligned panels. A screenshot comparison across all 10+ layouts catches these instantly. This is NOT pixel-perfect image comparison of rendered content (which is fragile) -- it's structural: "does the Editor look roughly right when I open layout 3 with the demo project?"

**Effort:** ~3-4 days

**Key insight -- the infrastructure already exists:**
- `ProgramWindows.CopyUiContentToShareTexture()` copies the full UI backbuffer to a GPU texture every frame (used by "Mirror UI on 2nd view")
- `ScreenshotWriter.StartSavingToFile()` saves any `Texture2D` to PNG via `TextureBgraReadAccess` (async GPU readback + WIC encoding)
- `LayoutHandling.LoadAndApplyLayoutOrFocusMode()` switches layouts programmatically
- Layouts are stored as JSON in `.Defaults/layouts/layout{0-12}.json`
- The Editor already has `--force-recompile` flag -- adding `--autotest` is straightforward

**Approach:**

A JSON test spec defines the sequence:

```json
{
  "resolution": [1920, 1080],
  "referenceDir": ".Defaults/Tests/UiScreenshots",
  "threshold": 0.05,
  "warmUpFrames": 30,
  "steps": [
    { "layout": 0, "waitFrames": 10, "name": "layout0-default" },
    { "layout": 1, "waitFrames": 10, "name": "layout1-variations" },
    { "layout": 11, "waitFrames": 10, "name": "layout11-focus" },
    { "layout": 0, "composition": "guid-of-demo-comp", "waitFrames": 10, "name": "layout0-demo-project" }
  ],
  "resultFilePath": "test-results.json"
}
```

**Steps:**
1. Add `--autotest <spec.json>` command-line arg to `Editor/Program.cs` (alongside existing `--force-recompile`)
2. Create `EditorAutoTest` class in `Editor/Gui/` that:
   - After startup completes (post `FlagStartupSequenceComplete`), waits N warm-up frames for ImGui to settle
   - Iterates through test steps: switch layout, optionally navigate to a composition, wait for rendering to stabilize
   - Captures the UI backbuffer via `ProgramWindows.UiCopyTexture` (the existing "mirror UI" texture -- just needs `CopyUiContentToShareTexture()` to be called even without the viewer window)
   - Saves to PNG via `ScreenshotWriter.StartSavingToFile()`
   - In "update references" mode: saves as new reference
   - In "test" mode: loads reference PNG, compares using the same pixel-deviation logic as `VisualTest.cs` (already exists in the operator test infrastructure)
3. Write results to JSON file, exit with appropriate code
4. Reference screenshots stored in `.Defaults/Tests/UiScreenshots/`

**Future extension -- Session State Tests (synergy with #715):**

Once session state serialization lands (issue #715), test specs can include full editor state:
```json
{
  "session": "test-sessions/demo-graph-editing.json",
  "waitFrames": 10,
  "name": "session-demo-graph"
}
```

This restores which project is open, which composition is viewed, what's selected, camera position, visible output -- then screenshots. This turns session restore into both a user feature AND a test fixture system.

**Comparison approach:** NOT pixel-perfect. Use a generous threshold (e.g., 5% deviation) to tolerate minor font rendering differences across machines. The goal is catching structural regressions (missing windows, broken docking, empty panels) not anti-aliasing differences.

---

### Phase 6: Graph Operation Scenario Tests (HIGH impact, MEDIUM effort)

**What:** Higher-level integration tests that exercise multi-step graph editing scenarios using commands, simulating realistic user workflows.

**Why:** Most bugs aren't in single commands but in sequences: "add node, connect it, delete the source, undo twice." These compound interactions expose state corruption that single-command tests miss.

**Effort:** ~3-4 days

**Steps:**
1. Define 10-15 scenario scripts as test methods:
   - Create composition -> add 3 nodes -> connect in chain -> delete middle node -> undo -> verify graph integrity
   - Copy subgraph -> paste -> modify paste -> undo paste -> verify original unchanged
   - Add node -> set input values -> animate input -> remove animation -> verify value restored
   - Create connection -> drag to reorder multi-input -> verify indices
   - Combine nodes into sub-symbol -> verify internal connections -> undo -> verify restoration
2. Each scenario asserts:
   - Connection count and endpoints
   - Child instance count and IDs
   - Input values and animation state
   - Undo/Redo stack depth

---

### Phase 7: Programmatic MagGraph Interaction Tests (MEDIUM impact, HIGH effort)

**What:** Test the MagGraph state machine and interaction logic by driving `GraphUiContext` programmatically within a minimal ImGui frame.

**Why:** The state machine (Default -> HoldItem -> DragItems, connection snapping, placeholder opening) is where the most visible UI bugs occur. But it requires ImGui.

**Effort:** ~5-8 days

**Steps:**
1. Create a test host that initializes a minimal DX11 device + ImGui context (no visible window needed if using offscreen rendering)
2. Build a `TestGraphUiContext` that provides a real `MagGraphLayout` backed by test symbols
3. Drive state transitions programmatically:
   ```csharp
   context.StateMachine.SetState(GraphStates.HoldOutput, context);
   // Simulate mouse position
   ImGui.GetIO().MousePos = targetInputPosition;
   context.StateMachine.Update();
   // Assert state transitioned to DragConnectionEnd
   ```
4. Test critical flows:
   - Drag connection from output to input -> connection created
   - Drag node -> position updated -> snap to grid works
   - Open placeholder -> select symbol -> node inserted with connection
   - Multi-select + drag -> all nodes move
5. Assert final model state via Symbol inspection (not pixel comparison)

**Risks:** Requires DX11 context even for "headless" ImGui. May need a dedicated test fixture for ImGui initialization. Fragile if ImGui internals change.

---

### Phase 8: Action Recording and Replay (MEDIUM impact, HIGH effort)

**What:** Add an optional recording layer that captures user interactions as replayable scripts.

**Why:** Enables creating regression tests from real user sessions. When a user finds a bug, they can export a recording that becomes a test case.

**Effort:** ~5-7 days

**Steps:**
1. Create an `ActionRecorder` that hooks into `UndoRedoStack.AddAndExecute()` and logs:
   - Command type, constructor arguments (serialized as JSON)
   - Timestamp, frame number
2. Create an `ActionPlayer` that deserializes and replays command sequences
3. Add Editor UI toggle: "Record Session" / "Stop Recording" / "Save Recording"
4. Recordings saved as `.t3test` JSON files
5. Test runner loads recording, replays commands, asserts final state matches snapshot

**Limitation:** Only captures command-level interactions, not raw mouse/keyboard. Won't catch bugs in the ImGui interaction layer before commands are created.

---

### Phase 9: Operator Unit Tests (LOW-MEDIUM impact, MEDIUM effort)

**What:** Test individual operator `Update()` logic with known inputs and expected outputs.

**Why:** Operators are the content pipeline. A broken `[Multiply]` or `[SampleGradient]` affects every project. Currently only caught by visual regression.

**Effort:** ~3-5 days

**Steps:**
1. Create `Operators.Tests.csproj`
2. Build a minimal evaluation context mock (needs `EvaluationContext` with playback time, no GPU)
3. Test pure-math operators: verify output values for known inputs
4. For GPU operators: skip or use the Phase 4 visual test infrastructure
5. Focus on operators that have had regressions or complex logic

---

## Priority Summary

| Phase | Impact | Effort | CI-Runnable | Catches |
|-------|--------|--------|-------------|---------|
| 1. Command tests | HIGH | 2-3 days | Yes | Graph mutation bugs, undo/redo corruption |
| 2. Serialization tests | HIGH | 1 day | Yes | Data loss, corrupt project files |
| 3. CI gate | MEDIUM | 0.5 days | Yes | Prevents merging broken code |
| 4. Visual regression CLI | HIGH | 1-2 days | Needs GPU | Shader/rendering regressions |
| 5. **Editor UI screenshot** | **HIGH** | **3-4 days** | **Needs GPU** | **Layout breakage, missing windows, UI glitches** |
| 6. Scenario tests | HIGH | 3-4 days | Yes | Multi-step interaction bugs |
| 7. MagGraph interaction | MEDIUM | 5-8 days | Needs DX11 | UI interaction bugs, snapping, drag |
| 8. Action recording | MEDIUM | 5-7 days | Partially | Real-world regression reproduction |
| 9. Operator unit tests | LOW-MED | 3-5 days | Partially | Operator logic bugs |

**Recommended order:** Phases 1+2+3 first (achievable in ~4 days, immediately valuable). Then Phase 5 (Editor UI screenshots -- high bang for buck, reuses existing infrastructure). Then Phase 4+6. Phases 7-9 are longer-term investments. Phase 5 becomes even more powerful once session state serialization (#715) lands.

## What This Won't Catch

- Pure visual regressions (wrong color, misaligned pixels) -- covered by existing VisualTest system
- Race conditions in async shader compilation
- Platform-specific GPU driver bugs
- User experience issues (confusing UX, wrong cursor, tooltip text)
- Performance regressions (need separate benchmarking)

The goal is not 90% coverage. Even Phases 1-3 alone would be a significant improvement -- a smoke test that catches the most common class of regressions (broken commands, corrupt serialization) before they reach users.
