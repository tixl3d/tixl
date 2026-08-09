# Make bypass work for field / shader-graph operators

**Status: done — implemented and live-tested 2026-08-09** via the central `Slot<T>` route (step 3). Steps 1 and
3 landed; step 2 (29 per-op edits) was **not** needed, and the five equivalent audio-op edits were reverted.
Only the two non-code items under §Risks are still open.

Original framing follows. Small, well-understood fix — the same two defects already found and fixed for the
audio graph on 2026-08-09 (see the "Bypass actually bypasses" note in
[`Plan_AudioProcessingGraph.md`](Plan_AudioProcessingGraph.md)). This plan exists because the shader graph has
one extra concern audio doesn't — generated code — and because the op count is larger.

## Problem

Bypassing a field operator does nothing. The editor draws the strike-through, the operator keeps working.

Two independent defects, both inherited by `ShaderGraphNode` from the same generic machinery:

1. **Bypass never engages.** `ShaderGraphNode` is listed in `Symbol.Child._bypassableTypes`, which is what makes
   `IsBypassable()` return true and the editor offer bypass. But `Instance.SetBypassFor`
   (`Core/Operator/Instance.Connections.cs`) dispatches on a type switch — `Command`, `Texture2D`, `float`,
   `Vector2`, `Vector3`, `string`, `BufferWithViews`, `MeshBuffers`, and now `AudioGraphNode` — and has **no arm
   for `ShaderGraphNode`**. Execution falls through every case, no update action is swapped, and
   `Symbol.Child.SetBypassed` records `_isBypassed = true` regardless of the result. The UI state and the actual
   behaviour disagree, silently.

2. **Un-bypass would leave the op neutered.** `Slot<T>.ByPassUpdate` overwrites the output's `Value` with the
   upstream node; `RestoreUpdateAction` restores only the *action*, not the value. Every field op assigns
   `Result.Value = ShaderNode` **once, in its constructor**, and its `Update` never re-publishes it — so after
   un-bypassing, the output would keep pointing at the upstream node forever. Fixing (1) without (2) turns a
   dead feature into a trap.

## Scope

29 operators have a `ShaderGraphNode` as both their first input and their first output, so they are the ones
the editor offers bypass for. **All 29 publish `Result.Value` in the constructor only.**

| Group | Ops |
|---|---|
| `field/adjust` | AbsoluteSDF, InvertSDF, NoiseDisplaceSDF, PushPullSDF, SetSDFMaterial, SpatialDisplaceSDF, TranslateUV, `_SDFToColor_Old` |
| `field/space` | BendField, ReflectField, RepeatAxis, RepeatField3, RepeatFieldAtPoints, RepeatFieldLimit, RepeatPolar, RotateAxis, RotateField, TransformField, TwistField, Translate |
| `field/combine` | BlendSDFWithSDF, CombineFieldColor\*, CombineSDF\*, StairCombineSDF\* |
| `field/use` | SDFToColor, SdfToVector |
| internal (`_`) | `_ExecuteSdfToColor`, `_ExecuteSdfToColor_Old`, `ExecuteRepeatFieldAtPoints` |

\* first input is a `MultiInputSlot` — see the known limit below.

Generator ops (`field/generate/**`: BoxSDF, SphereSDF, …) declare a `Vector3`/`float`/`Texture2D` first, so
`IsBypassable()` already returns false for them. Correct — there is nothing to pass through.

## Fix

1. **Add the switch arm.** In `Instance.SetBypassFor`, add a `case Slot<ShaderGraphNode>` beside the
   `AudioGraphNode` one, following the identical shape (`TrySetBypassToInput` / `RestoreUpdateAction` /
   `InvalidateConnected`). One block, copied from the arm added for audio.
2. **Re-publish the node from `Update`.** In each of the 29 ops, assign `Result.Value = ShaderNode;` at the top
   of the update method rather than relying on the constructor. Mechanical, and scriptable — the audio fix used
   a line-wise script that inserts after the method's opening brace and preserves each file's line endings
   (the repo is mixed CRLF/LF; see the bulk-edit rules in `AGENT_INSTRUCTIONS.md`).
3. **Consider fixing it centrally instead.** Before doing 29 edits, weigh whether `Slot<T>` should restore the
   pre-bypass `Value` in `RestoreUpdateAction` — stash it next to `_keepOriginalUpdateAction`. That would fix
   the whole class of bug for every current and future graph-node type in one place, and would make the audio
   ops' per-update assignment redundant. It touches shared slot machinery used by every operator in TiXL, so it
   needs its own risk assessment — but 29 copies of the same line is a strong argument for it.

**Recommendation:** do (1) and (3) first and test; fall back to (2) only if the central fix proves unsafe.

### What landed

(1) and (3). `Slot<T>` gained `StashValueForBypass()`, called from `TrySetBypassToInput` (and from
`TransformCallbackSlot`'s override, the only subclass that overrides it), with the symmetric restore at the top
of `RestoreUpdateAction`. `Instance.SetBypassFor` gained the `Slot<ShaderGraphNode>` arm.

Deliberately scoped to the **bypass** path via its own `_hasValueBeforeBypass` flag rather than piggybacking on
`_keepOriginalUpdateAction`, which is shared with the *disable* and *animation-override* paths. Those replace
the update action but never overwrite `Value`, so restoring a stashed value there would be at best pointless
and at worst resurrect a stale reference for a frame.

(2) was dropped: the five audio ops that had been given a per-update `Result.Value = _node;` were reverted, so
there is exactly one mechanism rather than two overlapping ones. **Consequence: audio bypass now rests on the
same code path and must be re-tested alongside the field ops** — see `.tests-manual/audio-graph-routing.md`.

## What is already handled

**Recompilation on bypass.** The obvious shader-specific worry — that bypassing changes the emitted code but
the root keeps a stale compiled shader — does not apply. `ShaderGraphNode.Update` builds `StructureHash` from
`_instance.SymbolChildId.GetHashCode()` folded with each child's hash
(`Core/DataTypes/ShaderGraphNode.cs`, ~line 143), and raises `ChangedFlags.Structural` when it differs from the
previous value. A bypassed node drops out of the traversal, so its child id disappears from the chain, the hash
changes, and `GenerateShaderGraphCode` regenerates. The same applies in reverse on un-bypass, **once defect (2)
is fixed** — without it the node never reappears in the traversal at all.

Worth confirming in the live test rather than trusting the read, since a stale shader would look like "bypass
does nothing" and send the next person down the wrong path.

## Known limit (do not design around it)

A bypassed **combinator** passes through its **first input only**. That is inherent to the generic mechanism
(`output := input[0]`) and is exactly how `Command` multi-inputs — e.g. a bypassed `[Group]` — already behave.
`CombineSDF`, `CombineFieldColor` and `StairCombineSDF` will therefore drop all but their first field when
bypassed. Consistent beats clever here: diverging for the field graph alone would be its own trap. Document it
in those ops' descriptions instead.

## Risks

- **Behaviour change for existing projects.** Anyone who has bypassed a field op today sees no effect and may
  have left it bypassed, assuming it was a no-op. After the fix those ops genuinely drop out, so a saved project
  can look different on load. Worth a release-note line.
- **`_`-prefixed internal ops.** The three `Execute*` ops are implementation details of user-facing symbols.
  Bypassing them is probably meaningless; check whether they should be excluded from bypass entirely rather than
  made to work.
- **The central fix (step 3) touches `Slot<T>`**, which every operator uses. If taken, it needs a broad smoke
  test — bypass and disable on Command, Texture2D and float ops at minimum.

## Verification

**No test set was written — a deliberate call, not an oversight.** Bypass is a single toggle with an immediately
visible result, so a written walkthrough would restate what the interaction already shows; the fix was verified
live instead. The cases below are kept as the checklist to work through if this area is ever touched again:

- Bypassing a `space` op (e.g. `[TwistField]`) in a raymarched chain removes its effect from the render, and
  un-bypassing brings it back — repeatedly, not just the first time.
- Bypassing an `adjust` op mid-chain leaves the rest of the chain intact.
- A bypassed `[CombineSDF]` passes its first field through (the documented limit), and un-bypassing restores
  both.
- Bypass state survives save/reload, and a project saved with a bypassed op loads with it still bypassed and
  still actually bypassed.
- Bypassing does not leave a stale shader: the change is visible immediately, without nudging another parameter
  to force a recompile.

## Reference

The audio-graph equivalent, including the switch arm to copy and the per-update re-publish, landed 2026-08-09 —
see `Instance.Connections.cs`, `Operators/Lib/io/audio/{CombineAudio,AudioReverb,AudioEcho,AudioCompressor,AudioLevel,AudioReaction}.cs`,
and the "Bypass actually bypasses" note in [`Plan_AudioProcessingGraph.md`](Plan_AudioProcessingGraph.md).
