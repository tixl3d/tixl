# Plan: [BlendSnapshots] operator — procedural snapshot triggering & blending

Goal: let a project drive the snapshot cross-fade procedurally (from LFOs, audio, MIDI) instead of only
by hand in the Variations window. Resurrects and finishes the `research/control-snapshot-op` experiment.

## The editor-only wall

Snapshots / variations are an **editor-only** system: `SymbolVariationPool`, `Variation`, and every blend
command live in `Editor/`, and the blend mutates `SymbolChild.Input` values through `ChangeInputValueCommand`.
The `Player/` project has *zero* variation/snapshot code, so any snapshot-driving operator does nothing on
export. (Undo is **not** the wall — the live blend uses `MacroCommand.Do()/Undo()` directly and never touches
the undo stack; only an explicit commit would.)

This gives three levels of ambition:

- **A — editor-only forwarding op (shipped).** Operator forwards weights+indices to the editor, which applies
  the existing blend machinery. Works live in the editor; no-ops on export.
- **B — editor-only transient slot-override evaluator (future).** Compute blended values and push them onto
  instance input slots during evaluation (like animation), bypassing commands/undo/document mutation and the
  per-frame allocation churn. The architecturally correct model for a *procedural* blend.
- **C — Core-side, player-capable (future).** Move the `Variation` data type + variation-file loader into
  `Core/` and build a Core-side blend evaluator (reusing B's slot-override) so the op works in an exported
  player. The command layer stays editor-only.

Decision: ship **A** as a clearly-labelled editor-only v1; keep the data format additive so B/C don't force a
migration.

## A0 — [ActivateSnapshot], the simple starting point (2026-06-14)

The full `[BlendSnapshots]` API (lists + weights + modes) proved hard to get under control as a first step, so
the simpler **`[ActivateSnapshot]`** op is the recommended entry point: two inputs, `Index` (int) and
`SetTrigger` (bool). On the rising edge of `SetTrigger` it applies the snapshot at `Index` taken **modulo** the
number of snapshots (reading order), exactly like a launchpad pad press (`pool.Apply` → undoable, sticky). No
blending, no lists, no release semantics.

- `Operators/Lib/io/input/ActivateSnapshot.cs` (+ `.t3`/`.t3ui`) — leaf op, `Result` Command output.
- `Core/IO/SnapShotBlendingData.PendingActivationsByComposition` — one-shot `compositionId → rawIndex` channel.
- `Editor/.../VariationHandling.ProcessSnapshotActivationRequests()` drains it each frame →
  `SnapshotActions.ActivateSnapshotByModuloIndex(rawIndex)` (ordinal-mod resolution; never creates).

Open question: `pool.Apply` adds an undo entry per trigger (same as a MIDI pad press today). Fine for now;
revisit if live triggering spams undo — a shared fix with the MIDI path. Manual test:
[`activate-snapshot-op.md`](../../.tests-manual/activate-snapshot-op.md).

## A — what shipped (2026-06-14)

Data flow mirrors the `ITapProvider` / `ForwardBeatTaps` forwarding pattern:

- `Core/IO/SnapShotBlendingData.cs` — a per-composition `BlendRequest { Enabled, Mode, Indices, WeightFactors }`
  the operator writes, plus a `ResolvedStatus*` the editor writes back for the operator's `IStatusProvider`.
  The op **re-arms** `Enabled` every evaluation; the editor **consumes** it after applying — so a deleted /
  disconnected / unevaluated op releases its blend after one frame (no stuck blends).
- `Operators/Lib/io/input/BlendSnapshots.cs` (+ `.t3`/`.t3ui`) — leaf op (no SubTree pass-through, by design),
  `Result` Command output. Inputs: `Enable`, `SnapshotIndices` (`List<int>`), `WeightFactors` (`List<float>`),
  `Mode` (`ControllerIndices | SnapshotIndices`, default ControllerIndices). Implements `IStatusProvider`.
- `Editor/.../VariationHandling.cs` — `ProcessSnapshotBlendRequests()` runs each frame: resolves indices →
  variations per mode, drops zero weights, normalizes to sum=1, drives the blend, releases when disabled, and
  writes the status verdict back. Allocation-free buffers; skips redundant re-applies when the resolved mix is
  unchanged (steady hold costs nothing).
- `Editor/.../SymbolVariationPool.cs` — `ApplyOperatorDrivenBlend(...)` replaces the pool's weight vector with
  the operator's normalized weights (so the picker faders + thumbnails reflect the procedural mix) and applies.
  `IsBlendDrivenByOperator` flag marks op ownership.
- `Editor/.../VariationPicker.cs` — weight faders go read-only (cursor + tooltip) while op-driven; they still
  show the live weight.

### Design decisions
- Mode default **ControllerIndices** (= `ActivationIndex`): stable across reordering, matches the MIDI grid.
- Weights normalized internally (the snapshot blend is a raw weighted sum, so weights must sum to 1).
- Op owns the pool while enabled; manual faders are locked rather than fighting it.
- `Weights` → **`WeightFactors`** (proportional, normalized — the name says so).

### Known A limitations (motivate B/C)
- **Editor-only**: no effect in an exported player. (→ C)
- **Reuses the hover machinery**, so it temporarily rewrites authored `SymbolChild.Input` values — saving the
  project mid-blend would capture the blended values. (→ B fixes this with slot overrides.)
- **Per-frame allocation** inside `CreateWeightedBlendSnapshotCommand` when the mix changes (the steady-hold
  case is already skipped). (→ B)
- Single active driven composition at a time (matches the editor's single `ActivePoolForSnapshots`).

### Possible follow-ups (not started)
- String-search → index custom dropdown for `SnapshotIndices` (pick a snapshot by name; convert to index).
- "Auto" indicator on the picker / control view when a blend is op-driven.

Manual test set: [`blend-snapshots-op.md`](../../.tests-manual/blend-snapshots-op.md).
