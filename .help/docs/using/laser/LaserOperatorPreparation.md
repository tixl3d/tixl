# Adding a laser operator

Checklist for adding a graph-side laser operator to the Lib package. The output side
(transports, safety, optimization) is **not** operator territory - it lives in the
[Output Setup](../OutputSetup.md); only content-side nodes (cameras, converters,
receivers) are operators.

## 1. Symbol source files

Place the implementation, definition and UI description under the laser namespace:

```
Operators/Lib/Symbols/laser/<category>/MyOp.cs
Operators/Lib/Symbols/laser/<category>/MyOp.t3
Operators/Lib/Symbols/laser/<category>/MyOp.t3ui
```

Existing categories: `camera` (scene-to-frame), `input` (protocol receivers),
`convert` (laser <-> graph buffers). Keep the namespace `Lib.laser.<category>` - the
LASER master button keys on it to decide whether to show itself.

## 2. Identities

Generate fresh GUIDs for the operator class and for every input/output slot that
declares one. GUIDs are forever: two symbols sharing an id corrupt projects.

## 3. UI layout

Keep the parameter panel flat (no `GroupTitle` headings) and give every input a
`Description` - it doubles as the tooltip and the documentation source. The `.t3ui`
entries must match the code's input GUIDs exactly; missing entries fall back to code
defaults.

## 4. Defaults

`InputSlot<T>` has no `DefaultValue` property - provide defaults via the `.t3` JSON,
constructor defaults, or guards in `Update`.

## 5. Documentation

Add the operator reference page under `.help/docs/operators/lib/laser/<category>/`
(overview, inputs & outputs, quirks, example) and register it in
`.help/docs/operators/index.json` so `[OpName]` autolinks resolve. Link the new page
from the relevant pages in `.help/docs/using/laser/`.

## 6. Robustness rules

Laser operators run inside the output pump's evaluation - they must never assume a
kind caller:

* catch and log around network I/O and GPU readbacks; degrade to an empty frame,
  never throw into the evaluation,
* listener threads are background threads with a single catch-all loop (see
  [PONKInput](PONKInput.md) for the pattern: join-free teardown via socket close,
  dead-thread auto-recovery),
* register with `LaserContentRegistry` on construction and unregister on dispose -
  the registry is thread-safe and snapshot-read.

## 7. Verify

* `dotnet build Operators/Lib/Lib.csproj` after Core API changes - the editor loads
  the package dll and a stale one crashes at first use,
* `dotnet test Core.Tests` - the laser protocol and safety tests must stay green on
  Windows and Linux,
* launch the editor and place the op in a scratch project before committing.

## 8. Commit

Use the repo's `op:` scope, e.g. `op: [MyOp] converts ...` - see `git log` for the
style.
