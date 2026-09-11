# Typed connection reroutes in MagGraph

Status: implemented. The sections below describe the delivered design; section 11 records implementation details, verification, and remaining manual acceptance work.

Source baseline: `d969679f6000d18a635333b59450c52cc681e23a`, TiXL 4.3, .NET 10. Paths below are relative to this repository root. The supplied Blender GIF is a visual reference; the requested gestures and architecture below define the feature.

## 1. Goal and scope

Add compact, movable routing anchors to MagGraph:

- **Shift + RMB drag:** collect crossed connections and merge branches from each source output through a typed reroute operator.
- **Ctrl + RMB drag:** cut crossed connections.
- Anchors are real operator children with real typed input/output slots. Their wires are ordinary `Symbol.Connection` objects.
- Each completed gesture is one undo step. Previewing or cancelling a gesture does not mutate the graph.
- Save/load, duplication, copy/paste, and playback use existing operator and connection infrastructure.

Keep `Core/`, serialization, project formats, migrations, Player, and the legacy graph unchanged. Shared Editor commands receive only the two reroute-specific guards documented in section 11. New operator definitions are necessary content, but require no new runtime graph mechanism. Do not refactor or optimize neighboring code. Record unrelated findings in [routing-foundissues.md](routing-foundissues.md).

Two deliberately bounded choices keep the implementation small:

1. Reroutes remain semantic `MagGraphItem.Variants.Operator` items, with a separate, transient reroute display flag. They look like another variant without falling out of ordinary operator selection/deletion behavior.
2. Reroutes support manual movement, normal socket connections, and merging by dropping one anchor onto another of the same type. Magnetic block snapping, automatic insertion by dragging a node onto a wire, and preserve-wires dissolve remain outside scope. Automatic tree layout preserves manually placed anchors as obstacles.

## 2. Interaction contract

### Shift + RMB routing

The implemented behavior for a stroke crossing several source outputs is **one anchor per distinct source output**, including separate anchors for different outputs of the same type.

Example, where only the wires to B and C were crossed:

```text
Before                       After
A.out -> B.in                A.out -> R.in
A.out -> C.in                R.out -> B.in
A.out -> D.in                R.out -> C.in
                            A.out -> D.in
```

- Group by composition identity, source child/interface ID, source slot ID, and exact slot value type. Never group by type alone or by downstream target.
- Crossing one wire inserts one anchor. Crossing a subset of a fan-out changes only that subset.
- Crossing the same wire repeatedly collects it once. Distinct duplicate connections into a multi-input remain distinct occurrences.
- An anchor forwards one source. Independent sources must never be combined into its input, even when their types match.
- Use the centroid of each group's first crossing positions for initial placement, converted from displayed coordinates to canvas coordinates. Show the placement during preview. If anchors would overlap, apply deterministic, small spacing between groups and show the adjusted positions before release.
- Create a fresh anchor for each group. Do not traverse upstream reroutes to infer an ultimate source, fuse existing anchors, or modify untouched branches.
- Select the created anchors after success so they can be moved immediately. Retain the previous selection on cancellation or failure.

### Ctrl + RMB cutting

- Remove only crossed, existing connection occurrences. Keep ordinary operators; remove reroutes that lose their last incident connection as part of the same undoable edit.
- Support cuts on wires into or out of anchors and on ordinary wires.
- A stroke hitting nothing produces no command and no dirty flag change.

### Gesture ownership and cancellation

- Start in an editable, hovered MagGraph canvas, with the graph in its normal idle state and no popup, active widget, or background-image interaction owning input.
- Reserve Shift/Ctrl RMB at mouse-down, before canvas panning processes it. Start collecting after the normal drag threshold. A modified click without a drag makes no edit and consumes its release.
- Latch the tool at mouse-down. Modifier changes during the drag do not switch tools.
- Ctrl + Shift + RMB is a consumed no-op; avoid ambiguous merge-versus-cut precedence. Alt combinations retain existing behavior.
- Escape, focus loss, composition/project change, package reload, popup takeover, or a structural edit during the stroke cancels it. Leaving the graph viewport cancels rather than cutting across another window.
- Keep ownership through the release frame: suppress RMB selection and context-menu opening, fence selection, competing socket gestures, graph keyboard mutations, and canvas panning/zoom while active. If cancelled while RMB remains held, discard the preview immediately but keep consuming that button until release; do not hand the unfinished press to panning.
- Keep this consumed-until-release latch in the `MagGraphView` partial, separate from per-context preview state, so navigation/reload replacing `GraphUiContext` cannot rearm the same press. Reserve the press across MagGraph views as well, so entering another graph cannot start panning. Clear it once release is observed, including after focus returns.
- Revalidate editability and the captured graph state on release. If any collected occurrence became stale or any required reroute definition is unavailable, cancel the entire gesture with a short reason; do not partially rewrite it.

### Anchor interaction

- A small type-colored body, one real input on the left, and one real output on the right. A disconnected socket remains available while the other side is connected; both remain available on a newly added blank anchor.
- Initial body size: approximately 16 by 16 canvas units, with final screen scaling following existing TiXL canvas/UI scale rules. Tune this within the existing draw file after visual inspection.
- Body click/drag selects and moves the ordinary child. Socket drag uses the existing input/output connection states, compatibility checks, replacement behavior, and cycle prevention.
- Give input, body, and output distinct hover regions; a larger hit target must not let the last registered socket capture both sides.
- Selection/hover outlines and a tooltip provide feedback. The tooltip can show the retained type and source name. No title row, thumbnail, custom UI, or normal node badges are needed on the compact body.
- Default Delete removes the reroute and incident wires through ordinary node deletion. It does not reconnect its neighbors.
- Disconnection does not change the concrete type. Reconnecting another type is rejected through normal type checking; implicit type changes or wildcard reroutes are outside scope.
- When a completed disconnect action leaves an anchor with no input or output connections, delete it in the same undo entry. This applies to manual wire disconnection, Disconnect, shake, and cutting. Check final state after rewiring rather than deleting in an individual connection command. Do not sweep anchors during drawing or remove newly added blank anchors before they can be wired.

## 3. Verified constraints that determine the design

| Current source | Consequence |
| --- | --- |
| `Core/Compilation/AssemblyInformation.TypeInfoExtraction.cs` reflects declared slot fields; `Core/Operator/Symbol.Child.cs` uses the normal parameterless constructor path. | Use concrete operator types with declared slots. Do not depend on open generics or inherited generic slot declarations. |
| `Editor/UiModel/EditorSymbolPackage.cs` associates a source file with its first operator GUID. | Keep one concrete operator per `.cs`; packing many operators into one file would undermine normal source lookup. |
| `Core/Operator/Instance.Connections.cs` resolves `Guid.Empty` endpoints as the composition's own input/output slots. `Slot<T>.ConnectedUpdate` forwards `GetValue(context)`. | A real, empty compound operator can forward its input to its output with an ordinary internal connection. |
| `Operators/TypeOperators/TypeOperators.csproj` already includes operator content using globs and is available to playback. | Add ordinary definitions there without changing its project file or creating an Editor dependency in runtime operators. |
| `MagGraphItem.AddToSelection`, `Modifications.DeleteSelection`, and many other MagGraph paths distinguish `Variants.Operator`. | Add a display discriminator, not a new semantic variant requiring a broad switch audit/refactor. |
| `MagGraphItem` and connection layout calculate sockets using full-size grid dimensions. | Compact drawing alone is insufficient; anchors, final wires, temporary wires, and hit tests must share compact coordinates. |
| `Editor/Gui/Interaction/ScalableCanvas.cs` pans on modified RMB and can also drag-zoom on Shift + RMB when `MiddleMouseButtonZooms` is false. | Intercept in MagGraph before `UpdateCanvas`; use `PreventMouseInteractions` during ownership to suppress both. `PreventPanningWithMouse` alone does not stop drag zoom. Leave the shared canvas unchanged. |
| Horizontal/vertical connection drawers construct `ImDrawList._Path` before stroking it. | Test against the actual displayed path through a small optional query hook, rather than approximating every cable as a straight line. |

### Type coverage gate

Before implementing the gestures, build an explicit list of supported concrete types from `Core/Model/SymbolPackage.TypeRegistration.cs`, the UI input/output factories, and actual slot declarations. There are 78 explicit built-in `RegisterType(typeof(...))` declarations at this baseline; this is an inventory starting point, not a promise that all 78 have identical forwarding requirements.

Record for each candidate: exact CLR type, owning assembly/package, compatible input/output UI, ordinary versus special consumer semantics, reroute symbol GUID, and slot GUIDs. Include numeric types, vectors, strings, lists, textures, buffers, meshes, commands, and applicable graph-reference types. Cover built-in types usable through ordinary input/output slots; do not silently reduce this to float and texture only. Types defined by optional/custom packages need definitions in a package that can reference those types; do not add package dependencies to TypeOperators just to force universal coverage.

Unsupported types must fail preflight without changing any wire. Supporting arbitrary future CLR types through code generation at gesture time is outside scope: it would add compilation, source creation, package reload, and symbol-lifetime behavior to a mouse action.

### Two forwarding cases that need explicit treatment

**Command callbacks.** A normal compound/value-copy forwarding op does not preserve first-use command preparation. Existing command consumers read `slot.Value.PrepareAction` before `GetValue(context)`, while a fresh compound output has not acquired its upstream value yet.

Use one dedicated, ordinary `RerouteCommand` implementation:

- Initialize its `Slot<Command>.Value` once with a stable `Command` containing forwarding prepare/restore delegates.
- In each delegate, resolve the current immediate upstream command slot through the input's existing connection API, and invoke its current value's matching callback without evaluating it first. Use the input's declared default when disconnected.
- The output's update action pulls `Input.GetValue(context)` once. Keep the stable output command object; do not replace it with the input value.
- Do not traverse arbitrary upstream operators, replay commands during connection edits, or add Editor-side evaluation behavior.
- This preserves the `Command` type and delegates its lifecycle, but intentionally does **not** preserve command-object reference identity. Ordinary resource/list reroutes do preserve reference identity.
- Prove first pull, changed callbacks, reroute chains, repeated/fan-out pulls, rewiring, disconnect, disabled/bypass behavior, and prepare/update/restore ordering before declaring Command supported. If these cannot be satisfied inside this operator, record the limitation and revisit the operator design; do not modify Core or command consumers as a shortcut.

**Composition multi-input bundles.** A composition `MultiInputSlot<T>` can expose several upstream values through a single visible source endpoint. Putting an ordinary `InputSlot<T>` / `Slot<T>` reroute in that path can collapse its multiplicity: `MultiInputSlot.GetCollectedTypedInputs` flattens direct multi-input slots, not ordinary output slots. Merely marking a `MultiInputSlot<T>` field as an output is insufficient because the loader classifies `IInputSlot` fields as inputs first.

The initial scope therefore rejects routing a bundle source, while allowing ordinary single-value sources connected to any number of multi-input **target occurrences**. Cutting those existing bundle wires is still possible. This is a visible compatibility limit required by the no-Core constraint, not an unrelated bug fix. Supporting bundles later requires a separate design decision. Also validate graph-reference and specialized-slot consumers by behavior: matching `ValueType` alone does not establish transparent execution or transfer clip/transform metadata.

## 4. Operator definitions and identification

### Ordinary forwarding definition

Place new operators under `Operators/TypeOperators/Symbols/Routing/`, using one stable symbol GUID and two stable slot GUIDs per concrete type. Never alter an existing value/texture operator to make it look like a reroute.

The standard operator contains declared `InputSlot<T>` and `Slot<T>` fields, implements the marker, and has no custom update method. Its `.t3` contains an ordinary connection:

```text
source child = Guid.Empty, source slot = Input GUID
target child = Guid.Empty, target slot = Output GUID
```

Values are pulled through normal slot evaluation. Reference values are forwarded without cloning, allocating replacement resources, taking ownership, or disposing upstream objects. Value types copy normally. Do not force `Always`/`Animated` dirty triggers or add per-frame runtime traversal. Command is the explicit operator-level exception described above.

### Explicit marker without changing Core

Add `Operators/TypeOperators/Utils/IRerouteNode.cs`, with a public marker interface in a stable TypeOperators namespace. It contains no Editor references, evaluation behavior, or mutable state.

Editor discovery verifies the known package identity plus the interface's fully qualified name on the operator type, and validates the declared one-input/one-output same-type shape. This avoids adding a static Editor-to-TypeOperators project reference. Do not detect by operator name, dimensions, `IExtractedInput<T>`, or merely having matching input/output types.

Build the type-to-definition lookup on demand for a gesture, outside drawing. Store definition/slot GUIDs and validate against the current registry. Reset lookup state when its package/assembly identity changes; do not retain unloaded `Type`/`Instance` objects indefinitely. Recompute each item's display flag when its layout references are rebuilt after a reload.

### File-count tradeoff

Budget conventional `.cs` / `.t3` / `.t3ui` triplets: **3N + 1 new operator files** for N supported concrete types plus the marker. Most are small, mechanical content files; existing operator files need no edits. Use a temporary authoring script if useful, but do not introduce a permanent generator or build pipeline just for this feature. Preserve GUIDs once created.

The loader can synthesize UI when `.t3ui` is missing, so `.t3ui` is not a serialization requirement. Nevertheless, normal source-file completeness tracking uses all three files, and `.t3ui` supplies descriptions and interface positions. Use triplets for the initial plan instead of saving files by weakening ordinary operator behavior. Any later reduction to `2N + 1` needs demonstrated loading, source lookup, save/reload, and package export equivalence without unrelated changes.

## 5. MagGraph presentation and geometry

1. Add a transient `IsReroute` (or equivalently small display enum) to `MagGraphItem`. Keep `Variant == Operator`, `Selectable == SymbolUi.Child`, real instance paths, real symbol references, real slot IDs, and exact type colors.
2. Set/reset identification during `MagGraphLayout.CollectItemReferences`. Keep ordinary visible-line collection, but force the single reroute input/output to remain available when disconnected. Apply compact size after the normal code that otherwise overwrites it with a grid-sized rectangle.
3. Make `GetInputAnchorAtIndex` and `GetOutputAnchorAtIndex` return left/right compact anchors, with one anchor per side. Put compact coordinate calculation here and reuse it. Do not retrofit every existing node-coordinate calculation.
4. Set the ordinary child `Size` through `AddSymbolChildCommand` at creation as well as the layout item's size. The two are separate: selection fitting and section growth can use the child, while fence selection uses the layout item. Persist position and size through their existing fields; add no serialized display flag. Avoid mutating persisted child sizes every frame.
5. In `UpdateConnectionLayout`, handle reroute-involved wires before full-size snapping heuristics. Use the corresponding compact endpoint on the reroute side and the appropriate visible-row socket on an ordinary endpoint. Set a nonsnapped `RightToLeft` style. Preserve damping using the same final endpoint targets.
6. Add a compact draw path after collapse/visibility checks in `DrawNode`. Preserve ordinary body and socket interaction registration; returning immediately after drawing a dot would make the anchor unusable. Register compatible sockets with `InputSnapper` / `OutputSnapper` and populate the same active item/slot/direction fields as ordinary nodes.
7. Use compact anchor helpers for temporary connections as well as final connections. Audit parameter-origin temporary-wire initialization in `MagGraphView` and change it only if its fixed initial position remains observable.
8. Suppress magnetic block behavior with local guards in `MagItemMovement`: skip reroute pairs in `TestItemsForSnap`, skip insertion targets that are reroutes, clear/disable splice sets for dragged selections containing reroutes, and reject reroute pairs in `GetPotentialConnectionsAfterSnap`. Ordinary drag movement and socket snapping remain available. Leave `MagItemMovement.Snapping.cs` unchanged.
9. In `TreeLayouting`, skip reroutes as layout targets and upstream placement sources, but include their compact bounds as fixed obstacles. Keep existing tree layout for other nodes. Do not allow auto layout to erase a user's route or place regular nodes over it.

Keep per-frame code allocation-free: no marker reflection, LINQ, per-node closures, string formatting when not hovered, or per-wire geometry copies. Use existing `UiColors`, type color variations, UI scaling, canvas transforms, and clipping conventions. Do not fix unrelated drawing allocations or grid-coordinate duplication.

## 6. Stroke geometry and state

Add one per-context stroke helper and one dedicated graph state. Keep routing-definition discovery and command construction in a separate feature helper so they do not enter the draw loop.

### Frame order

1. Before general keyboard actions and `UpdateCanvas`, reserve eligible modified RMB input and pass the existing `PreventMouseInteractions` flag to canvas interaction (or locally skip its interaction update). This suppresses both panning and Shift + RMB drag zoom; setting only `PreventPanningWithMouse` is insufficient. Enter the dedicated state before ordinary node drawing can act on RMB.
2. Compute normal layout/damping and draw the graph. During this gesture only, offer the newly traversed mouse segment to each eligible connection's displayed path.
3. Accumulate unique occurrence hits and preview anchors/cut highlights. Use no graph mutations during enumeration.
4. Process the release position as a final segment, finish all path queries, then validate and commit once after the connection enumeration. Preserve consumed-input state through context-menu handling at the end of the frame.

### Hit testing

- Test a stroke **segment**, not only the current cursor point, against each segment of the actual visible cable polyline. Handle zero-length segments, endpoint contact, and a small screen-space tolerance that follows UI scale.
- Add an optional reusable query argument to `GraphConnectionDrawer` and `VerticalConnectionDrawer`. Query the existing draw-list path immediately before every `PathStroke`, including early fallback branches. Default callers behave exactly as before.
- Do not retain references into `ImDrawList._Path` after the call. Pass a reusable query object/value or cached delegate; never create a capturing lambda per connection per frame.
- For `BottomToLeft` / `RightToTop` Bezier cases, use the same draw-list path to render and query the curve. Do not maintain separate approximate control-point math.
- For snapped connections, query the displayed connector marker, since there may be no visible cable length. A hit on an invisible straight line between centers must not count.
- Use the displayed damped positions and collapsed-section proxy endpoints. Clip tested segments to the draw list's visible rectangle before applying hit tolerance. Ignore fully hidden internal connections, fully clipped markers, unknown styles, and temporary wires. A visible section-boundary wire still identifies its actual underlying connection occurrence.
- Freeze canvas pan/zoom while collecting. Cancel if the graph structure changes; do not retain stale layout objects as the authority for a later edit.
- Keep reusable hit buffers and a short reusable preview trail. Size storage at gesture start from graph size; bound/decimate the displayed trail. Test each new mouse segment once and keep only the first crossing point for each occurrence. Do not rescan the full stroke history every frame.

## 7. Undoable graph edits

Use existing `AddSymbolChildCommand`, `AddConnectionCommand`, and `DeleteConnectionCommand`, composed into one routing-specific guarded command containing an ordered sequence of steps. The wrapper is nested in the feature helper; connection commands and the undo stack remain unchanged. `AddSymbolChildCommand` applies compact size when the added symbol is a reroute, covering both strokes and ordinary manual creation.

### Snapshot and preflight

- Capture the composition GUID/version and connections as value snapshots: source parent/child GUID, source slot GUID, target parent/child GUID, target slot GUID, and ordinal among connections to that target slot.
- Preserve `Guid.Empty` for composition interfaces. Use `MagGraphConnection.SourceParentOrChildId` / `TargetParentOrChildId`, not visual node IDs for interface nodes.
- Identify duplicates by occurrence ordinal. Neither `ConnectionHash` nor value-equal endpoint tuples distinguish repeated connections. Do not use `GetMultiInputIndexFor` for this operation; enumerate actual target occurrences.
- Validate all sources, targets, types, target ordinals, package editability, loaded reroute definitions, and slot contracts before the first mutation. Reject bundle sources for merge. Reject stale/ambiguous snapshots wholesale.
- Resolve graph objects from GUIDs at Do/Undo. Do not retain `Instance`, `Symbol`, or `SymbolUi` fields in the command. When the project/package is absent, log a warning and no-op defensively. This wrapper avoids invoking the existing deletion command's missing-symbol exception path.

### Merge command ordering

1. Allocate stable child IDs via one `AddSymbolChildCommand` per source group; set compact size and the previewed canvas position.
2. Add each child and its single original-source-to-reroute-input connection.
3. For each captured target occurrence, delete its direct connection and immediately add reroute-output-to-original-target at **the same target ordinal**. Immediate replacement keeps target counts unchanged; sort processing deterministically.
4. Group all source groups and replacements into one undo entry. Undo reverses replacements before removing source-to-reroute edges and children. Redo uses the original child IDs and positions.

The rewritten topology is an edge subdivision/fan-out factorization. It cannot introduce a cycle into an unchanged valid graph because each new node receives only its original source. Validate that invariant and retain normal cycle checks for later manual socket edits; do not build a second cycle-detection subsystem.

### Cut command ordering

Group captured occurrences by target endpoint, then delete in **descending target ordinal** within each group. Reverse undo restores ascending ordinals. This preserves remaining input order and duplicate edges even when one stroke cuts several inputs at the same target.

Append deletion steps for reroute endpoints with no surviving incident connection. Undo recreates those children before restoring their wires. Keep these steps inside the guarded routing command so failed preflight or rollback cannot leave an independent cleanup command deleting nodes.

### Failure and undo-stack behavior

`MacroCommand` is not a transaction: it does not roll back partial execution. Preflight must happen before execution. Keep the ordered subcommand list in the routing-specific wrapper so initial execution and redo can verify each step's expected child/connection postcondition. Existing add commands can warn and return without throwing; treat an unmet postcondition as failure too. Track completed subcommands and undo them in reverse if a step fails; include the failing step if inspection shows it already mutated the graph. Do not push a failed/empty edit. Implement this locally rather than changing `MacroCommand` for the whole editor or depending only on exceptions from `MacroCommand.Do()`.

Call the existing layout invalidation/selection update path after success, undo, and redo. Existing connection commands mark the symbol modified. Selection feedback can follow normal editor conventions; data, positions, child IDs, and connection occurrence order must round-trip exactly.

## 8. Planned file budget

This is a budget, not permission for adjacent cleanup. Keep edits limited to the named responsibilities. Count mechanical operator triplets separately from changes to existing behavior.

| File | Planned change |
| --- | --- |
| `Operators/TypeOperators/Utils/IRerouteNode.cs` (new) | Marker only. |
| `Operators/TypeOperators/Symbols/Routing/Reroute<Type>.cs/.t3/.t3ui` (new, N triplets) | Concrete forwarding definitions, including the dedicated Command implementation if its lifecycle checks pass. |
| `Editor/Gui/MagGraph/Interaction/RerouteOperations.cs` (new) | Cold definition lookup/validation, occurrence snapshots, merge/cut construction, and nested guarded undo command. |
| `Editor/Gui/MagGraph/Interaction/ConnectionStroke.cs` (new) | Per-context gesture lifecycle, incremental path queries, reusable hit/preview buffers. |
| `Editor/Gui/MagGraph/Model/MagGraphItem.cs` | Transient display flag and compact anchor geometry. |
| `Editor/Gui/MagGraph/Model/MagGraphLayout.cs` | Detection, compact size, always-available sockets, nonsnapped reroute endpoint layout. |
| `Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs` | Compact rendering and normal socket/body interaction; consume stroke RMB. |
| `Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawConnection.cs` | Query rendered paths/markers and preview affected wires. |
| `Editor/Gui/MagGraph/Ui/MagGraphCanvas.Drawing.cs` | Gesture ordering, view-owned consumed-button latch, temporary wire geometry, final commit/overlay. |
| `Editor/Gui/MagGraph/States/GraphUiContext.cs` | Own and reset the stroke helper with context lifetime. |
| `Editor/Gui/MagGraph/States/GraphStates.cs` | One state for an active routing/cut stroke. |
| `Editor/Gui/MagGraph/Interaction/MagItemMovement.cs` | Narrow guards against magnetic block operations involving reroutes. |
| `Editor/Gui/MagGraph/Interaction/TreeLayouting.cs` | Preserve anchors as fixed obstacles. |
| `Editor/Gui/UiHelpers/GraphConnectionDrawer.cs` | Optional query before existing path strokes; preserve existing rendering. |
| `Editor/Gui/UiHelpers/VerticalConnectionDrawer.cs` | Same optional path-query hook. |
| `.help/docs/using/KeyboardShortcuts.md` | Add a concise graph mouse-gesture section and explain routing/type limits. |
| `.tests-manual/graph-connection-splitting.md` | Extend the existing test set with routing, cutting, and regression cases. |

Target: **11 existing Editor files + 2 new Editor helpers + 3N + 1 operator files + 2 existing documentation/test files**, excluding these planning documents. One conditional additional edit is `MagGraphView.cs` if parameter-origin temporary connections need its initial coordinates corrected. Add focused automated tests to an existing suitable test file when its fixture can exercise the production operation; do not add a test framework, debug endpoint, or new project solely for this feature.

Do not touch `Core`, serializers, `SymbolUi.Child.Styles`, package format/version counters, shared canvas input, operator search behavior, input/output snapper internals, or the legacy graph. If another file becomes necessary, identify the specific routing failure it addresses before extending this budget. The implemented file-budget extensions are recorded in section 11.

## 9. Implementation sequence and acceptance gates

### Phase A — prove the operator boundary

- Complete the supported-type inventory and assign stable GUIDs.
- Implement representative ordinary reroutes: float, a mutable reference/list, a GPU resource, and Command's dedicated implementation.
- Verify unchanged ordinary slot types, identity for reference data, dirty propagation after source edits/rewire, command lifecycle order, save/load, and Player/package loading without Editor dependencies.
- Confirm bundle-source rejection and special-consumer limits explicitly. Do not begin broad type-file generation until the forwarding pattern is proven.
- Generate the remaining approved concrete definitions using the same pattern.

Exit: the real operators work independently of their compact UI and have a documented coverage list.

### Phase B — make one real operator a usable anchor

- Add marker recognition, transient display flag, compact geometry/rendering, and compact child size at creation.
- Validate disconnected sockets, left/right drag connections, body movement, selection, deletion/undo, duplication, and temporary wire alignment.
- Add the small magnetic/auto-layout guards and verify ordinary full-size nodes retain their behavior.

Exit: a manually added reroute behaves like a real typed node and renders as an anchor.

### Phase C — implement occurrence-safe edit operations

- Implement the cold merge/cut command builder and preflight without mouse-state dependencies.
- Verify exact connection tuples, multiplicity, and ordering for shared fan-out, duplicate edges, non-first multi-input occurrences, composition interfaces, and multiple source groups.
- Verify one-step undo/redo, repeat redo, missing-project guards, and failure rollback. Verify invalid requests leave graph/undo history unchanged.

Exit: command behavior is correct before attaching the gesture.

### Phase D — add strokes and visual feedback

- Wire gesture reservation before panning/keyboard handling, the dedicated state, displayed-path queries, preview, release commit, and cancellation.
- Reuse the same collector for both gestures; only preflight/action differs.
- Verify fast drags, curved/backward/vertical/snapped cables, final release segment, low/high zoom, UI scaling, collapsed sections, and no context-menu or selection leakage.

Exit: both requested gestures work end to end with one undo entry and no preview mutations.

### Phase E — validate and document within scope

- Build the affected TypeOperators and Editor projects using their existing build configuration and `.NET 10` requirements. Do not build over a loaded Editor configuration; follow the repository rebuild/restart workflow.
- Run existing relevant integration checks only after inspecting the fixture's project targeting. Any live probe belongs in `_agentTests`, never a user graph or Lib. Follow the debug-protocol skill before using the bridge; use manual input for gestures that its current API cannot exercise.
- Extend the existing manual connection test set and shortcut page. Keep generated operator-help Markdown untouched; operator descriptions belong in `.t3ui`.
- Compare new hot-path allocations/frame time while idle and during a stroke on a representative large graph. Optimize only a demonstrated regression introduced by this feature. Do not benchmark/refactor unrelated classes.
- Review the final diff against the file budget and confirm no Core/serialization/project-format changes. Keep additional discoveries in `routing-foundissues.md`.

## 10. Validation matrix

Move executable manual steps into the existing connection-splitting test set during implementation. These are the required acceptance categories, not a claim that tests have run.

| Area | Required evidence |
| --- | --- |
| Forwarding | Numeric equality; reference identity for list/resource data; null/disconnected defaults; source changes; reference replacement; dirty propagation; no owned-resource disposal. |
| Command | First prepare before pull; pull once per consumer evaluation; restore afterward; chains; fan-out; callback changes; disconnect/rewire; disable/bypass; no premature evaluation. |
| Merge | One wire; subset/all of a fan-out; two outputs of one node; different sources of the same/different types; existing reroute chains; repeated stroke crossings. |
| Multi-inputs | Middle/nonconsecutive target ordinals; identical duplicate edges; mixed merge/cut; exact undo/redo sequence; bundle-source merge rejected without mutation. |
| Boundaries | Composition input to child, child to composition output, both through reroutes; correct `Guid.Empty` mapping; optional/custom unsupported types fail clearly. |
| Gestures | Shift/Ctrl behavior; both `MiddleMouseButtonZooms` settings; consumed Ctrl+Shift; no-drag/no-hit; modifier changes; Escape; focus/window exit; popup; reload/navigation; read-only graph; release-frame suppression. |
| Geometry | Every visible cable style; snapped marker; rapid crossing; curved/backward connection; final mouse segment; damping; zoom/DPI; collapsed section boundary and hidden internal wires. |
| Ordinary node behavior | Body versus socket hit tests; forward/backward socket drags; cycle rejection; selection/mixed movement; duplicate/copy/paste; Delete/undo; anchor type retention. |
| Layout | No accidental block snap/splice/auto-wire; manual anchors stay fixed under tree layout; other nodes avoid anchor bounds; fit selection and section bounds use compact size. |
| Persistence | Save/reopen; hot reload; copy between editable compositions/projects with dependency handling; same IDs/positions/topology after undo/redo; package export and Player execution. |
| Performance | No new per-frame marker scanning, allocations, full-stroke rescans, or retained draw-list path data; bounded preview storage; graph mutation only on release. |

Completion means the declared supported types and gestures pass these checks, existing connection splitting/panning still work, and the documented bundle/custom-type limits are visible. It does not mean unrelated graph issues have been fixed.

## 11. Implementation and verification record

### Delivered implementation

- Added 78 concrete reroute operators covering the 78 exact CLR types in the built-in runtime registration. Each has its own stable symbol/input/output GUIDs and ordinary `.cs`, `.t3`, and `.t3ui` files. The source declarations and metadata in `Operators/TypeOperators/Symbols/Routing/` are the authoritative type/GUID inventory.
- The shared marker is `Types.Routing.IRerouteNode`, in TypeOperators. Recognition checks package GUID `c8a53b12-ded3-4327-86d2-bd731b25de22`, marker assembly identity, and the declared one-input/one-output contract. Marker reflection occurs when layout references are collected and definitions are requested, not for each rendered node each frame.
- Seventy-seven operators forward through their ordinary internal composition connection. `RerouteCommand` owns a stable callback proxy and delegates preparation/restoration to the immediate source without pulling early. It forwards values through the normal input update and owns no upstream resource.
- MagGraph keeps the semantic operator variant, derives an `IsReroute` display flag, and draws a 16-by-16 anchor. Actual transformed socket spacing determines the input/body/output partition, including low zoom and high DPI. Both persisted child size and layout size are compact; automatic tree layout and vertical obstacle stacks preserve the anchors.
- Shift+RMB and Ctrl+RMB share one incremental collector over the actual rendered paths. Preview highlights, snapped markers, clipping, bounded trail storage, gesture reservation/cancellation, and the release-frame commit are implemented in the existing MagGraph draw flow.
- The routing command preserves explicit target ordinals, exact duplicate occurrences, and source grouping. It validates all edits before mutation, checks each step's postcondition, rolls back partial failures or silent command no-ops, and stores one undo entry only after success. Redo retains the original child GUIDs and positions.
- Reusable hit/placement buffers and cached draw callbacks avoid per-cable callback allocations and whole-stroke rescanning. Source lookup and graph mutation run only on commit.

### File budget actually used

The original eleven existing Editor files and two new helpers were sufficient for geometry, rendering, layout, stroke collection, and graph rewrites. Three narrow existing Editor changes were necessary:

| Additional file | Concrete routing failure addressed |
| --- | --- |
| `Editor/UiModel/Commands/Graph/AddSymbolChildCommand.cs` | Reroutes created through ordinary add-node paths otherwise retained the full-size persisted child bounds, causing fit-selection/section bounds to disagree with their compact drawing. |
| `Editor/UiModel/Modification/NodeActions.cs` | Graph bypass would unnecessarily replace a Command reroute's callback proxy. Excluding anchors also avoids an empty undo entry when only anchors are selected. |
| `Editor/UiModel/Commands/Graph/ChangeInstanceBypassedCommand.cs` | Parameter panels, snapshots, popups, and the debug bridge call this command directly. Its reroute guard protects the same callback contract through those entry points. |

Also extended the existing `Tests/Editor.IntegrationTests/GraphAcceptanceTests.cs` with a durable regression test. The existing shortcut help and manual connection test document contain usage and acceptance steps.

The initial implementation changed **14 existing Editor files, 2 new Editor helpers, and 235 new operator content/marker files**. The automatic cleanup follow-up below adds one Editor helper and a narrow extension to `SymbolUi.External.cs`. The 234 small operator triplet files are required by the conventional one-concrete-type-per-source-file approach; no existing operator definitions were changed. Documentation and the existing integration test are counted separately. No changes were made to Core, Serialization, Player, project files, the shared canvas, the undo stack, connection commands, or snapper internals.

### Automatic removal after disconnection

- `RemoveDisconnectedReroutesCommand.cs` stores child/type/composition GUIDs and value/UI snapshots, removes only explicitly eligible fully isolated reroutes, and recreates them before connection undo. It preserves child identity, position, input values, output flags, name/comment, section membership, and snapshot/UI settings without retaining instances or package references.
- `GraphUiContext` captures connected reroutes when an edit macro starts. It appends cleanup only after the macro finishes, and discards the pending cleanup when the macro is cancelled. This permits temporary disconnection while reconnecting or choosing a replacement operator.
- `NodeActions.DisconnectNodes` appends the same cleanup after its existing disconnection/reconnection steps. `MagItemMovement` finishes a reroute's active move before shake can remove it, avoiding stale dragged-item references; movement and the disconnect retain their existing separate undo actions.
- `RerouteOperations` includes cleanup as guarded child-removal steps for cutting. Its preflight can resolve absent children from their recorded definitions on undo, and rollback covers the cleanup along with the wire changes.
- `SymbolUi.AddChild` receives an optional bypass-state argument for reconstructing a deleted child before live instances are created. Existing callers retain their defaults; this avoids losing an authored bypass state during cleanup undo.
- Candidate collection and snapshots run at explicit edit boundaries. Drawing does no orphan scan, and initially blank anchors are not eligible for unrelated cleanup. The existing manual connection test set contains last-wire, multi-output, chained-anchor, undo/redo, reconnection, and shake cases.
- Verification: the Debug Editor builds without warnings/errors. A 27-check isolated fixture using the actual compiled Editor graph and command types verifies macro completion/cancellation/reconnection, Disconnect, shake termination, last-wire cutting, selection cleanup, and undo/redo. A separate 26-check fixture loads TypeOperators normally and uses a live parent instance to verify exact metadata/default-value restoration, bypass/disabled/dirty flags in both model and live slots, redo, partial-connection retention, empty-anchor retention, and rejection of stale redo. The diagnostic fixtures live under ignored `artifacts/routing-ui-verification/` and `artifacts/routing-cleanup-verification/`; native mouse input is not part of these checks.

### Merge anchors on drop

- Limit implementation changes to `MagItemMovement.cs` and `RerouteOperations.cs`; reuse the existing guarded routing command, connection commands, and disconnected-anchor snapshot command. Update this plan, shortcut help, and the existing manual test document. No operator, rendering, Core, serialization, project, or general undo changes are needed.
- On release of a single dragged reroute, search visible, stationary reroutes of the exact same CLR type. Compare actual canvas centers against a 32×32 square centered on each target, inclusive at ±16 units on each axis. Choose the nearest eligible center, using the child GUID to break exact ties. Multiple-item drags and shake completion do not attempt merging. Search only on drop, with no additional per-frame work.
- Keep the stationary child and its identity, position, type, and settings. Transfer every outgoing occurrence of the dragged child to the stationary output at its original target ordinal, including duplicate multi-input connections. Keep the stationary input's source when both have external sources; otherwise retain the dragged input's source. Remove the internal edge of directly connected anchors and preserve the external source, without creating a self-connection.
- Validate editability, marker/slot contracts, captured wiring, and the proposed graph's cycle safety before mutation. Reject an incompatible, stale, or cyclic merge without changing connections. Use source/target snapshots and guarded steps for rollback and undo/redo; remove the absorbed child only after its cables have been transferred or removed.
- Store the movement's final values first, then append the executed collapse command to the same movement macro. Undo recreates the absorbed child's metadata and wires before restoring its original position; redo moves and collapses it again. Select the stationary survivor and skip hidden-input picking after a successful merge.
- Two newly blank anchors merge into one. Existing automatic last-connection cleanup still applies to anchors whose only cable was the removed internal chain edge. Incoming arrowheads and outgoing dot attachment remain unchanged.
- Verify source precedence/fallback, direct chains in both directions, fan-out, repeated multi-input ordinals, blank anchors, cycle rejection, stale undo/redo, one-step movement undo, exact 32×32 bounds, zoom/scale independence, hidden targets, mixed selection, and shake suppression. Extend the ignored actual-Editor fixture and the existing manual checklist rather than introducing another test project.
- Verification completed: the Debug Editor builds with zero warnings/errors, and the compiled Editor fixture passes 79 checks covering collapse and existing cleanup/cut behavior. These include source precedence/fallback, both chain directions, duplicate target ordinals, changed-topology cycle rejection on redo, movement undo/redo, ±16 canvas boundaries at 25% and 200% zoom, mixed selection, shake suppression, read-only rejection, and stale-reference cleanup for a chain with no external wires. Native mouse gestures and the full zoom/UI-scale matrix remain in the manual checklist.

### Automated verification

Build commands run from the workspace parent to avoid the unrelated invalid SDK version in the repository's `global.json`:

```powershell
dotnet build tixl/Core/Core.csproj -c Debug --nologo -v minimal
dotnet build tixl/Operators/TypeOperators/TypeOperators.csproj -c Debug --nologo -v minimal
dotnet build tixl/Editor/Editor.csproj -c Debug --no-restore --nologo -v minimal
$env:TIXL_DEBUG_PORT = '9042'
dotnet test tixl/Tests/Editor.IntegrationTests/Editor.IntegrationTests.csproj -c Debug --filter 'FullyQualifiedName~RerouteFloat_ForwardsChangesAcrossReloadAndConnectionUndo' --nologo -v minimal
```

- **Builds:** Core, TypeOperators, and Editor Debug builds succeeded with zero warnings/errors. Editor was stopped before replacing its loaded binaries and restarted for live verification.
- **Actual runtime operators:** the normal Core package loader loaded all 78 reroutes, matched the built-in type registry exactly, instantiated/evaluated their defaults, and serialized/reloaded/instantiated/evaluated all 78. Twenty behavior checks covered float dirty propagation, mutable/replaced/null list identity, a texture wrapper's reference identity without GPU allocation, and Command first-use, chains, fan-out, callback changes, rewire, replacement during pull, disconnected defaults, disable, and re-enable.
- **Graph rewrite fixture:** the production routing helper and add/delete connection commands passed exact duplicate/multi-input ordering, fan-out, distinct same-type sources, undo/redo, composition interfaces, bundle rejection, missing registry/reload-shape guards, injected post-mutation failures, silent no-op detection, and rollback during execution and undo. This fixture uses a small graph registry substitute; it is not an end-to-end Editor gesture test.
- **Actual ImGui drawing:** the final in-memory fixture passed 101 named checks with no failures. It invokes the built Editor's real connection drawers, collector, preview, and compact node draw helper. Coverage includes straight/curved/backward horizontal and vertical paths, both cubic styles, snapped markers, repeated crossings, duplicate occurrence identity, clip boundaries, and consumed-stroke suppression. Eighteen socket/body cases cover 25%, 50%, and 200% zoom at 100% and 200% UI scale. The fixture exposed and verified the fixes for offscreen tolerance hits and socket overlap at low zoom/high DPI.
- **Cross-view ownership:** real ImGui mouse events and the built Editor's reservation methods passed owner-first/owner-last release ordering, held-button suppression in another graph, hidden-owner cancellation, release-frame consumption, and stale-owner recovery. These use minimal contexts and initialized reservation state; they do not emulate a full valid graph commit. Separate isolated lifecycle checks cover exactly-once commit ordering and Escape cancellation.
- **Geometry/performance:** segment tests include 15 explicit cases and 5,000 randomized comparisons against an independent double-precision distance oracle plus symmetry checks. A warm loop of one million hit/miss queries allocated zero managed bytes. The actual collector plus dirty anchor-preview calculation allocated zero managed bytes across 500 in-memory ImGui frames. Reusable placement calculations also allocated zero managed bytes over 10,000 warm calls. These are focused microchecks, not a full-editor frame-time benchmark on a large graph.
- **Live Editor regression:** the existing bridge opens `_agentTests`, creates a typed float chain, checks disconnected fallback, connection undo/redo, input edits, project reload, stable child IDs/connections, and the direct bypass command's protection of a Command anchor. The probe chain remains in `_agentTests` for inspection.

Local diagnostic harnesses and reports are in ignored `artifacts/routing-operator-verification/` and `artifacts/routing-ui-verification/`; the isolated command harness is under the OS temporary directory. These are verification artifacts, not a new checked-in generator, framework, or build dependency. The live bridge regression is retained in the existing test suite.

### Remaining acceptance work and limits

- The native mouse gestures were not driven end to end in the live desktop window. The available debug bridge has no mouse-drag endpoint and the available computer-use surface has native controls disabled. The in-memory ImGui checks exercise production rendering and hit handling; the complete manual gesture/cancel/multiple-view checklist remains in `.tests-manual/graph-connection-splitting.md`.
- Clipboard operations, all collapsed-section/layout combinations, ordinary node split/pan regressions, cross-project dependency handling, and exported Player execution still need the listed manual acceptance checks. Normal runtime serialization/loading passed; that does not claim a completed exported executable test.
- The existing `BuildRenderRecolorUndo_RoundTrips` cube test was also attempted and failed its first screenshot content-size assertion. Its cause is unconfirmed; details are recorded in `routing-foundissues.md`. Its later recolor/undo assertions did not run.
- Routing from composition multi-input bundles, metadata-bearing outputs such as time-clip slots, and optional/custom types without a registered reroute is rejected before mutation. Existing editable bundle wires can still be cut. Anchors retain their original type and are not polymorphic after reconnection.
- Reroutes deliberately do not participate in block snapping or automatic splice/dissolve. Merging requires dropping a single anchor within the same-type target's 32×32 area. Ordinary editor bypass requests leave them unchanged; direct external manipulation of Core bypass state is outside this UI feature.
- The inherited double value-control mismatch and all other unrelated observations remain in `routing-foundissues.md`; they were not repaired as part of routing.
