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

- `reload` works on **editable projects only** (synchronous recompile; MSBuild errors come
  back in a `COMPILE_FAILED` detail). Built-in packages like `Lib` need an editor restart.
- `shutdown` is fire-and-forget — the response may never arrive.
- `getOutput` with a forced update only works for simple leaf evaluation and can
  double-evaluate; prefer the select-then-read pattern below.

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
   auto-saves. Do experiments in the `_agentTests` playground project (empty, fast to
   load, lives in the user projects folder outside the repo, and being editable it
   hot-reloads via `reload`), never in `Lib` or real user projects.
5. **The output camera persists — call `resetView` when screenshots look empty.**
   The view camera of a project's output window survives sessions; if it was ever
   dragged away, everything at the origin renders out of frame and screenshots
   show only the grid. `resetView` reframes the origin.
6. **`addOp` can steal the output view.** After adding an op, `screenshot` may fail
   with `NO_OUTPUT` because the new (non-renderable) op got focused. `pin` a
   renderable op (e.g. the draw op) to restore the output window.
7. **Hand-edited files vs. running editor.** Never hand-edit `.t3` / `.t3ui` / operator
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
