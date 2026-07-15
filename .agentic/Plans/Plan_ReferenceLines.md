# Reference lines — implicit relationships in the graph

Generalises the hover-only variable-link primitive that already exists (`OpUi.DrawVariableReferences`, tracked by [`Plan_GetVarHoverLink.md`](Plan_GetVarHoverLink.md) / issue #1077) into a **persistent, dashed, dedicated render pass** that covers every "wireless" relationship in the graph — Set/Get variables, audio auto-collection, and a future Send/Receive — with an optional action to make a link explicit. Consumed by [`Plan_AudioProcessingGraph.md`](Plan_AudioProcessingGraph.md) for its clip→root auto-collection lines.

## Goal

Make invisible relationships visible. Any implicit / wireless link between two operators renders as a faint dashed reference line, and — where it's semantically possible — can be turned into an explicit wire (or inlined). One affordance, several consumers; the reference/dataflow/direction distinction stays invisible to the user.

## What exists today

- `OpUi.DrawVariableReferences` (`Editor/Gui/OpUis/OpUi.cs:132-164`) — draws a straight solid line between a hovered Set/Get var op and its **name-matched siblings in the same composition**, in `UiColors.StatusAutomated`. Hover-only; callers are the per-type `{Get,Set}*VarUi.cs` in `Editor/Gui/OpUis/UIs/`. `Matrix`/`Object` var types have no custom UI, so they draw nothing.
- `Plan_GetVarHoverLink.md` (#1077, v4.2) — the narrow plan this supersedes.
- **No** dashed-line primitive (`grep dashed/DrawDashed` → nothing; ImGui has no native dashed line), **no** persistent relationship pass, **no** realize action.

## Set/Get variable mechanics (the model to match)

- Per-type pairs in `Operators/Lib/flow/context/` — `Get/SetFloatVar`, `…IntVar`, `…BoolVar`, `…StringVar`, `…Vec2/Vec3/Matrix/ObjectVar`.
- Binding is a **string name** (`VariableName` input). No GUID/object reference between a Get and its Set.
- Values live on `EvaluationContext` (five typed dicts, `Core/Operator/EvaluationContext.cs:155-169`), **cleared every frame** and scoped by the evaluation call-stack (Set can push/pop around a `SubGraph`). So the *true* Set→Get pairing is runtime- and order-dependent; the editor can only match **statically by name** (potentially many-to-many). That limitation is inherent and already lived-with by the hover link.

## Architectural decisions

- **Dedicated pass, not per-node hover.** New partial `Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawReferenceLines.cs`, called from `MagGraphView.DrawGraph` **right after the connection loop** (~`MagGraphCanvas.Drawing.cs:164`) so lines sit above real wires and under overlays. Iterate `_context.Layout.Items`.
- **Lightweight model, not `MagGraphConnection`.** That type is intrinsically slot-backed (`SourceOutput`/`TargetInput`/`AsSymbolConnection`). A reference line has no slots — introduce `MagReferenceLink { MagGraphItem Source; MagGraphItem Target; Color; LinkKind }`.
- **Two matcher kinds feed the link list:**
  - *Name-keyed:* Set/Get variables (`VariableName` equality), future Send/Receive. Static editor-side name match; many-to-many.
  - *Scan / singleton:* audio clip→root auto-collection (`AudioClipCollector` scans children for a root's sources) and audio analysis→reaction (the static `AudioAnalysis` / `AudioAnalysisResult` singletons — `_SetAudioAnalysis` → `AudioReaction`). No name; the link is "consumer ⟵ the collecting root / the single source."
- **Dashed rendering is net-new** — add a small segmented-line helper (repeated `AddLine`, or short beziers). Reuse two existing precedents: the collapsed-section **reroute-to-proxy** (`MagGraphCanvas.DrawConnection.cs:48-73`) for drawing to a stand-in when an endpoint is hidden, and `DrawOffscreenIndicators` for far endpoints. Colour: heavily-faded `UiColors.StatusAutomated` (matches today's link) or the value's type colour from `TypeUiRegistry`.
- **"Make explicit" is *per-kind* — the important caveat.** A linked pair usually does **not** share a slot to wire directly (e.g. `SetFloatVar` outputs a `Command`, `GetFloatVar` outputs a `float`):
  - *Audio clip→root:* clean — a real slot pair exists (the clip's `AudioReference` output → the root's `MultiInput`). **Realize** = `AddConnectionCommand` in a `MacroCommand`. The first-class case.
  - *Variables:* no shared slot. The meaningful action is **Inline the variable** — rewire the source feeding `SetVar.Value` directly to the consumers of `GetVar.Result`, then delete the two var ops. Only valid when a direct wire is topologically reachable (same/parent composition). Net-new logic over `AddConnectionCommand` + a delete-op command; distinct from audio's realize.
  - *Send/Receive (future):* design the ops **with a reference slot pair** so they get the clean audio-style realize, not the variable mess.
- **Undoable throughout.** All realize/inline actions go through `UndoRedoStack.AddAndExecute` wrapping `AddConnectionCommand` (undoable; `Do`→`Symbol.AddConnection`, `Undo`→`RemoveConnection`) + any delete commands in a `MacroCommand`. `Symbol.Connection` is the immutable 4-GUID model (`Core/Operator/Symbol.ConnectionSubClasses.cs`).
- **Ownership: editor-side.** This is a pure authoring/visualisation affordance — it lives in `Editor/Gui/MagGraph`; consumers register matchers. No `Core` runtime change.

## Consumers

| Consumer | Matcher | Make-explicit |
|---|---|---|
| Set/Get Variables | name-keyed | Inline (rewire-around + delete) |
| Audio clip → root (`Plan_AudioProcessingGraph.md`) | scan | Realize → `AddConnectionCommand` (clean) |
| Audio analysis → reaction (`_SetAudioAnalysis`→`AudioReaction`) | singleton | — (render only) |
| Send/Receive (future, VVVV/Houdini-style) | name-keyed | Realize (design with a slot pair) |

## Phases

### Phase A — Dashed reference-line pass for variables
Replace hover-only `DrawVariableReferences` with a persistent dashed pass in `MagGraphCanvas.DrawReferenceLines.cs`; name-keyed matcher for all Set/Get var types (incl. Matrix/Object, which draw nothing today); the dashed helper; the visibility policy. Supersedes `Plan_GetVarHoverLink.md`.
*Outcome:* variable links are persistently visible, dashed, across every var type.

### Phase B — Matcher abstraction + audio consumers
Generalise to a matcher interface (name-keyed + scan/singleton); register the audio clip→root and analysis→reaction links.
*Outcome:* audio auto-collection shows as reference lines; one pass serves both link kinds.

### Phase C — Make-explicit actions
Audio **Realize** (`AddConnectionCommand`, clean slot pair) and variable **Inline** (rewire-around + delete, guarded by reachability). Both undoable via `MacroCommand`.
*Outcome:* click a ghost line to make it a real wire (audio); inline a variable (vars).

### Phase D — Send/Receive ops (when prioritised)
Implement the named wireless-link ops with a reference slot pair so they render (name-keyed) and realize (clean) through this feature for free.
*Outcome:* VVVV/Houdini-style Send/Receive, visualised and realizable.

## Open questions

1. **Visibility default** — always-subtle-all vs hover/selection-only + a global "show relationships" toggle. Clutter risk on graphs with many vars.
2. **Many-to-many** — one Set name feeding N Gets (and vice versa): draw all lines, or a hub? Affects rendering *and* inline.
3. **Reachability for inline** — when a Get is in a different sub-tree the direct wire isn't possible; grey out inline, or offer partial?
4. **Cross-composition** — variables scope across nested comps via the eval-stack; render lines only within the current composition (like today) or across breadcrumbs?
5. **Sequencing vs #1077** — if `Plan_GetVarHoverLink.md` is mid-flight for v4.2, does this absorb it or land after? (Maintainer call — depends on #1077's status.)

## Relationship to other plans

- **Supersedes** `Plan_GetVarHoverLink.md` (#1077) — the hover link becomes Phase A of this.
- **Consumed by** `Plan_AudioProcessingGraph.md` — its clip→root reference lines + the first-class Realize case.
