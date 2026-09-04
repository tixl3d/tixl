# TiXL Debug Protocol — Driving the Editor from an Agent

Agent-neutral reference for the editor's TCP debug bridge. It lets any tool (Claude, Codex,
ChatGPT, plain scripts) launch the editor, build and inspect graphs, set parameters, take
screenshots, hot-reload projects, and run the visual reference test suite — no UI clicking.

Server code: `Editor/App/DebugProtocol/DebugServer.cs`. Typed .NET client:
`Tests/TiXL.DebugClient/`. xUnit tests that use it: `Tests/Editor.IntegrationTests/`.

## Launching

```
Editor/bin/Debug/net10.0-windows/TiXL.exe --debug-server 9042 --window 1600x900 --no-splash
```

- `--debug-server <port>` starts the bridge (localhost only). Without the flag it is off.
- `--window WxH` forces a predictable windowed size; `--no-splash` skips the splash screen.
- Startup until the port accepts connections takes roughly 10–20 s. Poll with retries.
- A running server shows a blinking IO icon in the app title bar; its tooltip lists recent
  protocol messages — useful when a human is watching an agent session.

## Transport

JSON lines over TCP on `127.0.0.1:<port>`. One request per line, one response per line:

```json
{"id":"1","method":"getVersion"}
{"id":"1","ok":true,"frame":1234,"playbackFrame":56,"structureVersion":7,"result":{...}}
```

- `id` is echoed back; process responses in order (the server answers sequentially).
- Every response envelope carries `frame`, `playbackFrame` and `structureVersion`
  (bumped on any graph-structure change) — use them to detect progress.
- Failures come back as `"ok":false` with an `error` string. Malformed lines are logged
  and answered with an error; the connection stays open.

## Methods

Read surface: `ping`, `getVersion`, `getStructureVersion`, `getMetrics`, `getContext`,
`getLogTail` (`minLevel`, `maxCount`), `getGraphState`, `getOutput`, `screenshot` (`path`).

Control surface: `openProject` (`name`), `newProject`, `select` (`childId`), `setInput`
(`childId`, `inputName`, `value`), `addOp`, `connect`, `deleteOp`, `pin`, `pumpFrames`
(`count`), `resetView`, `reload`, `undo`, `redo`, `setTime`, `setPlayback`, `shutdown`.

Parameter shapes are defined in `DebugServer.cs` — read the handler when unsure. Notes:

- **Start from what the user is looking at.** `getContext` returns the focused
  composition, `selectedChildren` (ids + names) and `outputView` (the op the output
  window shows, whether it is pinned, and the evaluation-start op if that differs).
  When the user says "this looks broken", read that first and probe those ids -
  don't rebuild the chain from a screenshot description.

- `reload` works on **editable projects only** (synchronous recompile; MSBuild errors come
  back in a `COMPILE_FAILED` detail). Built-in packages like `Lib` need an editor restart.
- `shutdown` is fire-and-forget — the response may never arrive.
- `getOutput` with a forced update only works for simple leaf evaluation and can
  double-evaluate; prefer the select-then-read pattern below.
- `getOutput` picks the output by `outputId` (guid), `outputName`, or defaults to the
  first output. Pass `update: true` to force a pull for outputs nothing displays.
- `getOutput` on a `MeshGeometry` slot returns a numeric summary instead of the
  data: point/face/corner/part counts, bounds, `boundaryEdges` /
  `nonManifoldEdges` (0/0 = watertight), signed `volume` (parts of a fracture
  must sum to the input's volume) and the attribute list. Use it to validate
  geometry ops without screenshots. Add `dumpObj: "<path>"` to also write the
  geometry as OBJ (one object per part, `# cut` before IsCut faces) for offline
  analysis of exactly which edges are open.
- `getOutput update:true` starts a fresh invalidation tick before pulling, so
  upstream changes propagate through the chain. Ops upstream with `Async` on
  still return their *previous* result while a job runs - set `Async` false on
  the whole chain when a probe must read a deterministic value right away.
- Rebuilding `Lib` right after a `shutdown`: the editor can rewrite operator
  `.cs` files on exit, which leaves them older than the previous DLL and makes
  MSBuild skip the compile. `touch` the edited files before building, and check
  that the built DLL actually changed.

## Hand-over and blocking dialogs

- `setAgentState` (`state`: `busy` | `ready` | `""`, optional `note`) drives the bridge
  icon in the app bar: magenta while an agent works, **green when it reports `ready`**.
  Send `ready` with a one-line note at the end of every verification round, before
  handing back to the user; any later request flips it to `busy` automatically.
- A modal message box (e.g. "can't create Input Definition for <Type>" when an op uses
  a Core type that isn't registered in `SymbolPackage.TypeRegistration.cs`) blocks the
  main thread, and with it the whole bridge: requests queue until the user closes it.
  There is no protocol call to dismiss it - register the type, or ask the user.

## Evaluation model (the part everyone trips over)

The editor is pull-based: **an operator only evaluates when something displays it each
frame**. Setting an input does not run the graph.

1. **Select to evaluate.** After building or changing a graph, `select` the op whose output
   you care about — the UI preview then pulls it every frame. Only then do `getOutput` /
   `screenshot` reflect current values.
2. **Pump frames after every change.** `pumpFrames` with count 10–25 after `setInput` /
   `select` / `openProject` before reading anything back.
3. **Trigger inputs need a flank.** A bool trigger fires on a false→true transition across
   frames: set `false`, pump, set `true`, pump. Setting `true` on an already-true input
   does nothing.
4. **Auto-save landmine.** `setInput` on symbols marks projects modified and the editor
   auto-saves. Do experiments in the **`_agentTests`** project (empty, fast to
   load, lives in the user projects folder outside the repo, and being editable it
   hot-reloads via `reload`), never in `Lib`, never in `playground`, never in a real
   user project. `playground` is the user's own scratch graph - probe chains left
   there are the agent's clutter in a graph someone is working in.
5. **The output camera persists — call `resetView` when screenshots look empty.**
   The view camera of a project's output window survives sessions; if it was ever
   dragged away, everything at the origin renders out of frame and screenshots
   show only the grid. `resetView` reframes the origin.
6. **Don't stack ops.** `addOp` without `posX`/`posY` starts a new row below the lowest
   op already in the graph (left-aligned, 200 px gap); `getGraphState` reports each
   child's `posX`/`posY`. Lay a chain out along that row with explicit `posX` steps
   from the returned position, and never reuse fixed coordinates across probe runs —
   the previous run's ops are still there, and a stack of ops on one spot is
   unreadable for the person reviewing the graph.
7. **`addOp` can steal the output view.** After adding an op, `screenshot` may fail
   with `NO_OUTPUT` because the new (non-renderable) op got focused. `pin` a
   renderable op (e.g. the draw op) to restore the output window.
8. **Hand-edited files vs. running editor.** Never hand-edit `.t3` / `.t3ui` / operator
   `.csproj` files while the editor has them loaded — it rewrites them on save. Close or
   cycle the editor first.

## Wire formats and addressing

- Values use Newtonsoft defaults: `Vector2/3/4` as `{"X":..,"Y":..}`, `List<int>` as
  `{"Values":[...]}` (see `Core/Model/SymbolPackage.TypeRegistration.cs`).
- `openProject` accepts a short-name prefix; created projects display as
  `"<name> (<namespace>)"`.
- Address ops by `childId` (GUID) from `getGraphState`; symbol names can be ambiguous
  after a symbol exists in two loaded packages — prefer ids then.

## Cycling the editor after a code change

Operator packages hot-reload via `reload`, but Editor/Core changes need a restart:

1. Send `shutdown` (fire-and-forget; the response may not arrive). Debug builds close
   immediately; teardown takes ~2–10 s — poll the process list.
2. `dotnet build` the affected project. Never build the configuration of a still-running
   editor (Debug incremental is safe once it exited).
3. Relaunch with the flags above and wait for the port.

## Running the visual reference test suite

- Quick protocol/graph checks: `dotnet test Tests/Editor.IntegrationTests --filter "Category!=VisualSuite"`.
- Full visual suite (~98 tests): `--filter "Category=VisualSuite"`. Requires a running or
  auto-launched editor (`TIXL_DEBUG_PORT` env var attaches to an existing one on that port).
- Individual tests carry stable hash ids (`#id` suffix in result lines). Known-flaky tests
  are muted via the `IgnoredTestIds` input on `[ExecuteTests]` — merge, don't replace, the
  existing list.
  `Tests/Editor.IntegrationTests/VisualSuiteTests.cs` shows the full
  select → flank-trigger → poll-results flow.
