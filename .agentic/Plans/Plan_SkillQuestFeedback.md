# Plan: SkillQuest Feedback System

## Problem

During a SkillQuest level the user can get stuck in three ways:

1. They accidentally modified a parameter that isn't part of the solution — they will never complete the level until it is reverted.
2. They modified a required parameter, but not to the correct value.
3. They don't know which parameter to change next.

The level itself can't surface any of this today. The only feedback is whether `_PlayModeProgress` reaches `1.0`, which is binary.

## Goal

Drive per-parameter and per-operator feedback in SkillQuest mode from a single, author-friendly source of truth, surfaced through the existing op-status and parameter-window UI.

## Non-goals (v1)

- Per-parameter authoring metadata (tolerance, custom hint text). Numeric distance and generic strings only.
- Detecting structural mistakes (deleted connections, extra ops). Parameter-only feedback.
- Multiple valid solutions per level.
- Authoring tools beyond what the existing Snapshot UI already provides.

## Ground truth

The level symbol holds **exactly one Snapshot/Variation**. It represents the solved state.

- Captures live in `Variation.ParameterSetsForChildIds : Dict<SymbolChildId, Dict<InputId, InputValue>>` — already the right shape.
- The level symbol's default parameter values (what's in the `.t3`) is the implicit *start* state. No second snapshot needed.
- Required-change set = keys present in the Solution variation.

### Validation at level load

- `0` snapshots → `Log.Warning("No solution snapshot defined for this level.", this)`. Feedback disabled, level still playable.
- `1` snapshot → use it.
- `2+` snapshots → `Log.Warning("More than one solution snapshot defined. Picking first.", this)`. Use the first.

## Per-frame state model

For each `(SymbolChildId, InputId)` encountered in the level composition:

| State | Condition |
|---|---|
| `Untouched` | current = default, key ∉ Solution |
| `Required` | current = default, key ∈ Solution |
| `Correct` | current = Solution |
| `Warm` | current ∈ between default and Solution (numeric, within fixed tolerance band) |
| `Forbidden` | current ≠ default, key ∉ Solution |

Tolerance: fixed fraction (e.g. 1% of the input's `[min, max]` range, or absolute epsilon if no range). No per-input override in v1.

"What's next" = first `Required` parameter in stable iteration order. Stickiness: once a parameter is focused, keep it focused until it leaves `Required` or `Warm` to avoid flicker between candidates.

## IStatusProvider extension (cross-cutting)

The existing `IStatusProvider.StatusLevel` enum is `{ Undefined, Success, Notice, Warning, Error }`. Renderers in `MagGraphCanvas.DrawNode.cs` and `GraphNode.cs` skip `Undefined` and `Success`.

Add `Tip` (between `Success` and `Notice`):

```csharp
enum StatusLevel { Undefined, Success, Tip, Notice, Warning, Error }
```

Touch points:
- `Core/Operator/Interfaces/IStatusProvider.cs` — enum.
- `Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs:1049–1062` — add `Tip` case (icon `Icon.Tip`, color `UiColors.StatusAttention`).
- `Editor/Gui/Graph/Legacy/GraphNode.cs:90–103` — same case (legacy graph, lower priority but keep parity to avoid divergence).
- `Success` rendering is intentionally *not* enabled in this change — leave it gated as today, but documented as available for future use (e.g. shader compile success).

This extension is decoupled from SkillQuest and benefits any operator that wants to emit a non-warning hint.

## Runtime feedback engine

New file: `Editor/Skills/Training/LevelFeedback.cs`.

```csharp
internal sealed class LevelFeedback
{
    public void OnLevelLoaded(Instance compositionRoot, SymbolVariationPool pool);
    public void Rebuild();  // walks composition once, fills state dict
    public bool TryGetParameterState(Guid childId, Guid inputId, out ParamState state, out float warmth);
    public bool TryGetOpStatus(Guid childId, out IStatusProvider.StatusLevel level, out string? message);
    public bool TryGetNextFocused(out Guid childId, out Guid inputId);
}
```

- Owned by `SkillTrainingContext`; lifetime bound to level load/exit.
- Gated on `SkillTraining.IsInPlayMode` ([SkillTraining.cs:469](../Editor/Skills/Training/SkillTraining.cs:469)) — outside play mode the snapshot on the level symbol behaves as a normal user variation. The accessor returns true for both `Playing` and `Completed` states, which is what we want: feedback stays visible after the level is solved (all parameters should read `Correct`).
- `Rebuild` is cheap (iterate child inputs, dict lookups). Initially called every frame inside `SkillTraining.PostUpdate`; promote to revision-driven invalidation only if it shows in a profile.
- No LINQ, no allocations in the per-frame path — pre-sized dicts cleared and refilled.

## UI integration points

1. **Op badge** — `MagGraphCanvas.DrawNode.cs` status block (around line 1049) consults `LevelFeedback.TryGetOpStatus` *in addition to* the op's own `IStatusProvider`. Forbidden parameter → `Warning`. Required-and-untouched op (any of its children) → `Tip`. SkillQuest source wins ties.

2. **Parameter line icon** — add an inline `Icon.Tip` / warning icon next to the revert slot inside `InputValueUi<T>.DrawParameterEdit` ([InputValueUi.cs:112](../Editor/UiModel/InputsAndTypes/InputValueUi.cs:112), called from [ParameterWindow.cs:576](../Editor/Gui/Windows/ParameterWindow.cs:576)) when `LevelFeedback.TryGetParameterState` returns `Required`, `Warm`, or `Forbidden`. Tooltip text from a small static string table per state.

3. **"What's next" hint** — the focused parameter from `TryGetNextFocused` gets a stronger emphasis (e.g. pulse from the shared `Blink` source) on its op badge and parameter line. Only one focused target at a time.

## Authoring workflow

1. Author opens a level symbol.
2. Tunes parameters until output is correct.
3. Captures a Snapshot in the existing Variations panel.
4. (Optional) Tests by entering SkillQuest play mode and resetting to defaults — feedback should light up the required set.

No new authoring UI in v1. The validation warnings at load time are the only feedback for malformed levels.

## Risks and open questions

- **Snapshots only store changed inputs** — confirmed at [SymbolVariationPool.cs:304](../Editor/Gui/Interaction/Variations/Model/SymbolVariationPool.cs:304) and [:378](../Editor/Gui/Interaction/Variations/Model/SymbolVariationPool.cs:378) (both skip `input.Input.IsDefault`). So the required-change set is exactly the snapshot's keys; no diff against default needed at load.
- **`ExcludedFromPresets` inputs are silently skipped on capture.** If a level author marks a solution parameter as excluded, it will never appear in the Solution snapshot and feedback will never flag it. Not v1-blocking — surface as a warning later if it becomes a real authoring trap.
- **Non-blendable types are skipped on capture** ([SymbolVariationPool.cs:319](../Editor/Gui/Interaction/Variations/Model/SymbolVariationPool.cs:319)) — same trap as above for things like string / reference inputs. Level authors who need to teach editing those parameters will hit a wall; flag in docs.
- **Non-scalar parameters.** Texture / list / reference inputs have no numeric distance → `Warm` collapses to `Correct`/`Required` (equality only). Fine for v1; tolerance band only meaningful for scalars and vectors.
- **Multiple instances of the same symbol child.** Snapshots key by `SymbolChildId`, so this is one-to-one per composition. Should be fine for the simple level shapes in use today.
- **Hot reload / level restart.** `OpenedProject.Reload` on exit means the `LevelFeedback` instance must be discarded on `ExitPlayMode`. Already gated by the IsInPlayMode check, but worth verifying no stale references survive a reload.
- **Stickiness algorithm.** Naive "first required" can flicker if two parameters become required simultaneously. Keep current focus until it resolves; only re-pick when the current one transitions to `Correct` or out of the required set.
- **Float / vector equality is bit-exact.** `AreEqual` in `LevelFeedback` uses `ValueUtils.CompareFunctions`, which compares floats directly. A user-entered value like `0.5` will not match a snapshot value of `0.49999998` and the parameter stays `Warm` forever. A custom compare with a small tolerance band (per scalar type, possibly normalized against input range) would fix this. Deferred: most current tutorials use round numbers, so typed values likely round-trip exactly; revisit if real levels expose the gap.
- **Tip ordering by severity.** When multiple feedback items are eligible to surface as a hint, `Warm`-with-tiny-gap should rank *last*. The `_PlayModeProgress` completion check has its own threshold, so a near-correct float may already end the level before the user needs a nudge about it. Sort priority roughly: `Forbidden` > `Required` > `Warm` (large gap) > `Warm` (small gap).
- **Gradient comparison.** `Gradient` inputs are currently excluded from the classifier because `AreEqual` falls back to `ToString` and produces phantom mismatches between visually identical gradients. A proper comparison would sample the gradient at N evenly spaced positions (e.g. 100), sum the per-channel color differences, and treat anything under a tolerance as equal. Same approach would give us a `GetTipProximity` value for gradient tips. Until then any level that depends on a gradient parameter cannot use feedback.

## Phases

All six landed. Code lives in [LevelFeedback.cs](../Editor/Skills/Training/LevelFeedback.cs) and [SkillQuestParameterHint.cs](../Editor/Skills/Training/SkillQuestParameterHint.cs); manual tests in [.tests-manual/skill-quest-feedback.md](../.tests-manual/skill-quest-feedback.md).

1. ✅ **IStatusProvider extension** — added `Tip` to the enum, wired both MagGraph and Legacy node renderers, decoupled from SkillQuest.
2. ✅ **Read-only `LevelFeedback`** — state derivation, validation warnings on load, per-frame `Rebuild`, observable via the `[LevelFeedback] R=… W=… C=… F=…` fingerprint log.
3. ✅ **Op badge integration** — MagGraph node consults `TryGetOpStatus`; Forbidden → Warning, focused Tip's op → Tip badge; others stay quiet.
4. ✅ **Parameter-line icon** — overlay at the name-button revert slot via `SkillQuestParameterHint.DrawIcon`; tooltip prefix folded into the existing description tooltip via the `Hint?` parameter on `DrawInputTooltipAndResetIcon`.
5. ✅ **"What's next" focus** — sticky single tip with categorical relevancy (bool > enum > numeric, then position), per-key fade-in timers, tour-gate (Info/InfoFor suppress hints; CallToAction/Tip/Conclusion let them through), proximity arc with log scaling + gain/bias, drag-aware focus pinning.
6. ✅ **Manual test set** — [skill-quest-feedback.md](../.tests-manual/skill-quest-feedback.md). Opens by teaching the new `Ctrl+Shift+Alt+T` / `Ctrl+Shift+Alt+L` debug-window shortcuts that make the runner reachable during play mode.

### Beyond plan

Picked up along the way and worth noting:

- Snapshot-enabled indicator on the MagGraph node is hidden in play mode — every solution op is snapshot-enabled, so the badge would just spoil "what to touch."
- Output window forces the "Fill" resolution preset in play mode so level projects don't open at their saved 1920×1080.
- "Effectively default" classification — Forbidden now requires the value to actually differ from default, not just have `IsDefault == false`. Toggling a bool back to its default value or typing the default into a field correctly clears the warning.

## Out of scope (deferred)

- Per-parameter tolerance overrides and custom hint text — revisit if real levels need it.
- Detecting structural mistakes (connections, op presence) — different mechanism (parallel hidden solution graph), separate plan.
- Score / time-to-solve metrics — would feed into `SkillProgress.LevelResult` but unrelated to feedback.
- Time-gated visibility (the "delay before showing focus" idea from the source proposal) — only add if real flicker is observed.
