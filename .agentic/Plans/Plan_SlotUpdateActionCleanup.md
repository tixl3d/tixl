# Slot update-action state machine consolidation

**Status:** Drafted 2026-08-09. Not scheduled — queued for after the 4.3 timeline work quiets down.
A `Core` evaluation-path refactor; land it alone with a full regression pass, like
[`Plan_TimeClipEvaluation.md`](Plan_TimeClipEvaluation.md) Phase 1 was.

## Motivation

`Slot<T>`'s update-action override machinery (disable, bypass, animation override, connection swap,
restore) stores its saved state in loose fields (`_keepOriginalUpdateAction`, `_keepDirtyFlagTrigger`,
`_actionBeforeAddingConnecting`) that every participating path must remember to save and restore
symmetrically — and subclass overrides (`TimeClipSlot`, `TransformCallbackSlot`) each re-implement a
subset. One week of timeline work surfaced **three** independent traps of exactly this shape:

1. **Null-restore wedge** — `RestoreUpdateAction()` assigning `UpdateAction = null` through
   `TimeClipSlot`'s wrapping setter left a permanently broken slot warning *"Ignoring invalid time clip
   update action"* every frame. (Fixed: the setter special-cases null.)
2. **Trigger wipe on re-enable** — `TimeClipSlot.SetDisabled` / `TransformCallbackSlot.SetDisabled`
   stashed the update action but not `_keepDirtyFlagTrigger`; the base `RestoreUpdateAction` then
   "restored" the never-set field, erasing `DirtyFlagTrigger.Animated` so a re-enabled clip never
   evaluated again. Latent forever; armed when `[VideoClip]`'s texture output became the clip slot.
   (Fixed: both overrides stash the trigger.)
3. **Wrapper aliasing** — `TimeClipSlot`'s `UpdateAction` setter re-wraps whatever is assigned, so any
   base-class code that reads the getter and assigns the value back (`SetDisabled`, bypass, restore)
   risks stashing/rewrapping the wrapper itself. Currently held together by each override knowing to use
   `_baseUpdateAction` instead of the property. (Mitigated case-by-case, not structurally.)

The current fixes are point patches. The mechanism remains: N call sites × M subclass overrides each
maintaining an implicit contract with no single owner.

## Proposed shape

One private state holder on `Slot<T>` that owns the *complete* saved state and the transitions:

```csharp
private struct SavedSlotState   // sketch
{
    Action<EvaluationContext>? UpdateAction;
    DirtyFlagTrigger Trigger;
    // anything future overrides add lives here, in one place
}
```

- `PushOverride(newAction, reason)` / `PopOverride(reason)` (or equivalent single pair) replace the
  scattered save/restore in `SetDisabled`, `TrySetBypassToInput`, `OverrideWithAnimationAction`,
  `RestoreUpdateAction`, and `AddConnection`/`RemoveConnection`.
- Subclasses stop overriding `SetDisabled` for stash bookkeeping; they only contribute their wrapper via
  the existing setter (or a dedicated `WrapAction` hook), so base transitions can't miss subclass state.
- The reason/kind tag makes conflicting overrides (`disable` while `bypassed`) an explicit, logged case
  instead of the current silent `Log.Warning("Is already bypassed or disabled")` early-returns.
- The effective-action rebuild (`RebuildEffectiveUpdateAction`, sibling-output time remap) already goes
  through the setter and composes cleanly with this — it stays as-is.

## Risks

- The evaluation hot path (`Update`) must stay a single delegate invocation — this refactor only touches
  the (cold) transition paths, but the review must verify no per-frame cost sneaks in.
- Hidden order dependencies: hot reload (`RestoreUpdateAction` with null stash), compound-output
  connection forwarding (`_actionBeforeAddingConnecting`), and animation overrides layer on top of each
  other today in an undocumented order. Enumerate the legal state transitions first; the enumeration is
  half the value of the refactor.

## Regression net

The disable/re-enable, bypass, animate-input, hot-reload, and connection-rewire behaviors across:
`time-clip-evaluation.md` (step 8), `video-clip-player-*.md`, `undo-redo-graph-edits.md`, plus a manual
bypass/disable pass over an animated op and a `[TimeClip]` composition.
