# Tixl Debug Protocol — Implementation Plan

Goal: give an external client (Claude via CLI, scripts, eventually a test runner) read and control access to Tixl's live state — commands, graph state, logs, ImGui state, screenshots — over a simple local protocol, with hot reload in the loop. Tests come later as one client of this protocol; they do not shape it.

Sequencing (2026-09-02): a scoped subset (Phases 0, 1, 2 without `getUiState`, 3, 5, 6) is the agreed precursor to `Plan_ProceduralGeometry.md` Phase 1 — the geometry work's verification loop is the payoff. Phases 4 and 7 wait until the geometry phases have lived with the protocol.

Status (2026-09-02): **Phase 0 ✅** — see `tixl-debug-protocol-audit.md` (addressing, command classification, bypasses, infra map; key decisions: socket threads enqueue raw lines and the main-thread pump parses/executes/responds; envelope carries ImGui frame + playback frame + new global `SymbolUi.GlobalVersionCounter`; mutations dispatch commands directly, never UserActions). **Phase 1 ✅ (verified live)** — `Editor/App/DebugProtocol/DebugServer.cs` (+ `DebugLogBuffer.cs`), started via `--debug-server <port>` (`Program.cs`), pumped at the top of `T3Ui.ProcessFrame`; methods: `ping`, `getVersion`, `shutdown` (via `EditorUi.Instance.ExitApplication()` — `Application.Exit()` only triggers the exit dialog). **Phase 2 ✅ (verified live except happy-path getGraphState/screenshot, which need an open project — covered by Phase 3's openProject)** — `getLogTail` (ring buffer `DebugLogRing`, seq-numbered), `getGraphState {compositionId?, includeDefaults?}`, `getContext`, `getStructureVersion`, `getMetrics` (incl. per-process GPU memory via `Adapter3.QueryVideoMemoryInfo` — the leak-detection metric), `screenshot {path}` (async via `ScreenshotWriter`, responds on readback completion). `getUiState` skipped per subset scope. **Phase 3 ✅ (core, verified end-to-end)** — `openProject {name|symbolId, pinOutput}` (visible graph window, `TryCreateWithExplicitHome`, pins root), `select {childId}` (per-frame evaluation via UI preview — THE pattern for multi-frame ops), `setInput` (undoable `ChangeInputValueCommand`; list values wrapped `{"Values":[...]}`), `getOutput {childId?, outputId?, update?}` (forced pull only for leaf evaluation), `pumpFrames`, `setTime`, `setPlayback`. Acceptance exceeded: `Scripts/run-visual-tests.py` drives the full visual reference suite hands-off, incl. the new `#hashId`/`IgnoredTestIds` round-trip (98 tests, ~13s + startup; app-bar IO indicator with request tooltip; `--window <w>x<h>` / `--no-splash` launch flags). Learnings for later phases: protocol `setInput` auto-saves library symbols (add a transient/non-modifying write); trigger params need explicit false->true flanks across frames; an empty `_agentTests` user project is wanted as the standard playground (examples startup is heavy); `shutdown` response is fire-and-forget; per-category vs aggregate op instances make "which child" discovery matter (resolve via root-output connection, not symbol id). **Phase 3 complete ✅ (acceptance passed 2026-09-02)** — added `newProject {name}` (full `ProjectSetup.TryCreateProject` scaffold+compile; DisplayName comes back as "<name> (<namespace>)"), `addOp {symbolName|symbolId, posX, posY}` (name lookup across packages, AMBIGUOUS error lists candidates; returns `childId`), `connect` (slot resolution by name/guid/first, `Structure.CheckForCycle` guard), `deleteOp`, `pin {childId}`, `undo`/`redo`. `Scripts/protocol-acceptance.py` runs the plan's acceptance criterion end-to-end in the `_agentTests` playground project (created on first run at `pixtur._agentTests`; note: the project template is NOT empty — 14 children / 5 connections baseline): build CubeMesh->DrawMesh, pin, screenshot, recolor via setInput (Vector4 travels as `{"X":..,"Y":..,"Z":..,"W":..}`), verify the image changed, undo to baseline, assert clean log. **Phase 6 `reload` ✅ (verified)** — `reload {project}` calls `EditableSymbolProject.TryRecompile(updatePackage: true)` synchronously (widened to internal); success returns `durationSeconds` (~1s for `_agentTests`), compile errors come back verbatim in `COMPILE_FAILED` (verified: broken source -> exact MSBuild `CS1031` with file/line; fixed -> ok; unknown project -> NOT_FOUND with a hint that built-in packages need a restart). Blocks the frame loop while compiling — clients use generous timeouts. Only *editable* (user) projects reload; Lib/examples changes still need an editor cycle — which is why op development iterates in `_agentTests` first. The `_agentTests` template content was deleted via protocol (now 0 children — fast loading). `openProject` accepts the short project name (display names are "<name> (<namespace>)"). **Phase 7 arrived early (2026-09-02)** — usage shaped it exactly as intended: the Python probe scripts were replaced by proper .NET test infrastructure. `Tests/TiXL.DebugClient` (typed protocol client — wire formats centralized) + `Tests/Editor.IntegrationTests` (xUnit; `EditorFixture` attaches via `TIXL_DEBUG_PORT` or launches/tears down its own editor; tests serialized on one shared instance). 11 facts: protocol basics, the Phase 3 acceptance round-trip, reload paths, and the visual reference suite as `[Trait("Category","VisualSuite")]`. Quick run: `dotnet test Tests/Editor.IntegrationTests --filter "Category!=VisualSuite"` (~7s + startup); full run incl. suite ~30s + startup. See also `Plan_AutomaticTests.md` — this shaves its deferred xUnit yak; its Phases 1-2 (pure model tests) can now join the Tests/ folder. Remaining, deliberately deferred: Phase 4 log correlation, Phase 5 `tixlctl` CLI (the DebugClient library is its future core), CI wiring (needs GPU runner), `getUiState`.

## Field validation (2026-09-01)

An agent session verified a resource-leak fix (`SceneSetup.Dispose` had an inverted guard) by driving the editor with synthetic mouse input and screen captures — the exact workflow this protocol replaces. Concrete lessons folded into the phases below:

- **Pixel-guessing breaks on gesture ambiguity.** A double-click aimed at "empty canvas" was interpreted as leave-composition and silently navigated to the Projects hub. Screenshot-diffing recovered it, but each such misstep costs a full observe-decide-act round trip. `dispatch` avoids the entire class.
- **Synthetic input steals the user's mouse and keyboard.** The session had to take focus away from the user's concurrent work, and died the moment the user reclaimed the machine. Focus-free operation is now an explicit design decision, not a nice-to-have.
- **The leak itself went unnoticed because nothing measured it.** The broken `Dispose` shipped silently; verification required reading Windows GPU performance counters from outside the process. A cheap in-protocol metrics read turns this whole bug class into a one-line assertion — added to Phase 2.
- **What was actually needed that day:** launch, open a project, add two ops, connect them, click one trigger parameter repeatedly, screenshot, tail the log, read GPU memory. Phases 1–3 cover all of it; Phases 1–2 alone would have covered the observation half.

---

## Phase 0 — Audit (no code, ~half a day)

Inventory the mutation paths before building anything on them.

- List all `UserAction`s and `Command`s. For each: is it constructible from plain data (ids, values), or does it capture live object references?
- Identify mutations that bypass the command path (direct model edits from UI code). Don't fix them yet — just list them. They are the places where a state query will return data the command log can't explain.
- Identify the existing identity scheme: how are ops, symbols, instances addressed? The protocol needs stable string/GUID addressing for everything a client can reference.

**Output:** a short doc: addressable entities, serializable commands, known bypasses.

---

## Phase 1 — Transport + skeleton (1–2 days)

The deliberately boring version.

- **Transport:** TCP on `127.0.0.1`, configurable port, enabled by `--debug-server <port>`. JSON-lines: one request per line, one response per line, each carrying a client-chosen `id`.
- **Threading:** the socket thread only parses and enqueues. All request execution happens on the main thread at a single point in the frame loop — after input, before draw. Responses are written back from there. No locks on model state, ever.
- **Envelope:**

```json
{"id":"a1","method":"ping"}
{"id":"a1","ok":true,"result":{"version":1,"frame":8842,"structureVersion":89}}
```

Every response includes `frame` and `structureVersion` for free — clients always know what state they observed.

- Methods in this phase: `ping`, `shutdown`, `getVersion`.

**Acceptance:** `echo '{"id":"1","method":"ping"}' | nc localhost 9042` returns while the app runs at full frame rate.

---

## Phase 2 — Read surface (2–4 days)

Highest-value first:

1. `getLogTail {sinceSeq?, minLevel?}` — log records from an in-memory ring buffer (JSON records, sequence-numbered). This plus dispatch kills most blind guessing.
2. `getGraphState {compositionId?, depth?}` — ops, connections, selection, parameter values. Reuse existing serialization where possible; readability over completeness in v1.
3. `getStructureVersion` — cheap polling primitive: "did anything change since I last looked."
4. `getContext` — active composition, current time, playback state, selected op.
5. `screenshot {target?: "window"|"output", path}` — write PNG to disk, return the path. Uses the existing screenshot capability.
6. `getUiState {panel?}` — ImGui state pre-draw: window/panel tree, widget rects for named items, focus, hover. Start minimal (panel layout + graph canvas item positions); grow on demand. This is the only surface that answers "why is the widget not where the graph state says."
7. `getMetrics` — frame time, GPU dedicated/shared memory for the process, and a few ResourceManager counts (live buffers, SRVs, textures). Cheap to serve, and it makes resource-leak regressions assertable ("memory delta ≈ 0 across N reloads") instead of invisible — the 2026-09 `SceneSetup.Dispose` leak would have been caught by exactly this.

**Acceptance:** with the app open on a project, a script can dump graph state, take a screenshot, and tail the log without touching the UI.

---

## Phase 3 — Control surface (2–3 days)

1. `dispatch {action: "...", args: {...}}` — construct and dispatch a UserAction/Command from data, using Phase 0's inventory. Response includes a `commandId` and the resulting `structureVersion`. Unknown/unserializable actions return a clean error naming what's missing — this becomes the living to-do list for command coverage.
2. `undo` / `redo` — through the existing queue.
3. `setTime {seconds}` / `setPlayback {playing}` — injected clock control.
4. `pumpFrames {count}` — advance N frames then respond; the primitive for "act, let it render, then look."
5. `openProject {name}` / `openComposition {symbolId}` — session bootstrap. Every scripted scenario starts here (the hub is otherwise only reachable by clicking), so these land with the first dispatch actions, not later.

Semantics: a dispatched command is applied at the same frame-loop point as everything else; the response is sent after application, so `dispatch` → `getGraphState` is always read-your-writes.

**Acceptance:** create an op, connect it, change a parameter, screenshot, undo — all from a shell script, app never touched by hand.

---

## Phase 4 — Log correlation (1 day, do alongside Phase 3)

- Every protocol-dispatched command gets a `commandId`; a `volatile` ambient holds the currently-applying command.
- Log writer stamps each record with `{commandId?, structureVersion, frame}`.
- `getLogTail` gains `{commandId}` filter: "show me everything this command caused (synchronously)."
- Optionally thread `commandId` into async work spawned during application (`originCommand`) — do this lazily, when a real deferred-log case bites.

No invented step numbers — correlation uses the app's own causality.

---

## Phase 5 — Client tool (1 day)

A tiny CLI so the protocol is usable from bash (and therefore by me, in-session):

```
tixlctl dispatch AddOp '{"symbol":"Blur","composition":"..."}'
tixlctl graph --depth 1
tixlctl logs --since-last --min-level warn
tixlctl shot /tmp/after.png
tixlctl pump 3
```

Thin: parse args → send line → print response. Later, optionally wrap as an MCP server, but the CLI alone already changes dev sessions.

---

## Phase 6 — Hot reload in the loop

- `reload` method (or file-watch trigger) invoking the existing hot-reload path, responding with success/compile errors + timing.
- The compile error text in the response matters most — it closes the loop: edit → `tixlctl reload` → read errors or proceed to state queries.

**Acceptance:** the full loop — edit code, reload, dispatch, inspect state, screenshot — runs without restarting the app, each iteration in seconds.

---

## Phase 7 — Tests as a client (after living with Phases 1–6)

Only now, and shaped by actual usage:

- A cue file is just a recorded/authored list of protocol calls with expectations attached (log budgets, state predicates, optional image baselines).
- The runner is a standalone client — it launches Tixl with `--debug-server`, plays the script, checks expectations, writes a report. Nothing test-specific lives in the app.
- The recorder is the app-side exception worth adding: serialize the live command stream to a replayable script, so a manual repro session becomes a regression test.
- Authoring assertions use state + logs; visual baselines only where the state dump can't express the expectation.
- **First cue file, already scoped by the 2026-09 leak session:** open `_Tests`, dispatch `AddOp LoadGltfScene` + `AddOp DrawScene` + connect, pump frames, snapshot `getMetrics`, fire the `TriggerUpdate` parameter ×10 with pumps between, then assert: output non-black, no "Skipping draw call" warnings in the log tail, GPU memory delta ≈ 0. One small script exercises file loading, material creation, scene dispatch, disposal on reload, and the metrics/log/screenshot surfaces together — it should be the protocol's hello-world regression test.

CI (self-hosted GPU runner) is a config task once the runner exits nonzero on failure.

---

## Design decisions (fixed up front)

- **Localhost only, opt-in flag.** No auth in v1; the flag is the auth.
- **Focus-free by construction.** No method may require the window to be foreground, focused, or even visible; no synthetic OS input, ever. The user must be able to keep working in other apps while a client drives Tixl. (Falling back to synthetic mouse input in the 2026-09 session meant stealing the user's cursor mid-work and aborting when they took it back.)
- **Protocol versioned** from message one (`getVersion`), so the CLI and app can drift.
- **Errors are data:** `{"ok":false,"error":{"code":"UNKNOWN_ACTION","detail":"..."}}` — never a dropped connection.
- **All reads/writes on the main thread at one frame-loop point.** Simplicity beats latency here; human/agent timescale tolerates one-frame response times.
- **No test concepts in the app.** Steps, settling, baselines, cues — all client-side conventions, iterable without touching Tixl.

## Risks

- **Command serialization gaps** (Phase 0's bypass list) are the real schedule risk — the protocol is only as useful as the fraction of mutations reachable through it. Mitigation: the `UNKNOWN_ACTION` error path makes gaps visible and prioritizable instead of blocking.
- **Graph state dump size** on large projects — mitigate with `depth`/`compositionId` scoping from day one.
- **ImGui state extraction** is the most exploratory item; keep `getUiState` minimal and demand-driven rather than attempting a full mirror.
