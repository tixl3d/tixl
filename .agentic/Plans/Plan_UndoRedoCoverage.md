# Plan: Undo/Redo Coverage Gaps

This document inventories all user-facing model mutations that bypass the `ICommand`/`UndoRedoStack` system. Each gap is categorized by severity and estimated effort. The intent is to drive follow-up work to close these gaps -- ideally test-driven with the command-level integration test framework.

## Progress

**2026-04-22** -- Batch 1 pass: gaps #1-4 were already covered by `AnimationOperations` / `KeyframeCopyAndPasting` (they push `MacroCommand`s to `UndoRedoStack`); stale `// TODO: this should use Undo/Redo commands` comment removed in [InputValueUi.cs:343](Editor/UiModel/InputsAndTypes/InputValueUi.cs:343). Gap #10 closed via new `ChangeCommentCommand` and rework of `EditCommentDialog` to commit edits on dialog close. Gap #16 closed by replacing `cmd.Do()` with `UndoRedoStack.AddAndExecute(cmd)` in [NodeActions.cs:236](Editor/UiModel/Modification/NodeActions.cs:236). Automated regression tests deferred -- see note in [Plan_AutomaticTests.md](.agentic/Plans/Plan_AutomaticTests.md).

**2026-04-22** -- Batch 2 pass: gap #7 was already closed by recent commit `d890a482c` (in-flight `ChangeKeyframesCommand` on tangent drag). Gap #5 closed via new `SetInputDefaultCommand` covering both `InputValueUi` call sites. Gap #9 closed via new `ChangeSymbolDescriptionCommand`; dialog now buffers and commits on close. Gap #8 closed: `SetExtractedInputValuesCommand` added to the extraction macro so the extracted values are restored on redo; `ExtractAsConnectedOperator` now accepts an optional `collectInto` MacroCommand so the MagGraph path no longer produces a nested/duplicate undo entry. Added a `Guid` overload for `Symbol.InvalidateInputDefaultInInstances` in Core to avoid needing an `IInputSlot` handle from a pure command.

**2026-04-22** -- Batch 3 pass: gaps #6, #11, #14 closed. Gap #13 deferred pending deeper design. New `UpdateVariationParametersCommand` and `RemoveInstancesFromVariationsCommand` cover the previously-direct mutations in `VariationHandling.RemoveInstancesFromVariations` and `SymbolVariationPool.UpdateVariationPropertiesForInstances`; both accept a `collectInto: MacroCommand?` parameter. New `ChangeSnapshotEnabledCommand` plus the `collectInto` plumbing lets both context menus ("Enable for snapshots") combine the enable toggle and variation mutations into a single undo entry (legacy `GraphView` and `MagGraph/GraphContextMenu`). Gap #14 closed by wrapping the new-clip TimeRange mutation in its own `MoveTimeClipsCommand`; the "incomplete and likely to lead to inconsistent data" remark was removed.

## Gap Inventory

### CRITICAL -- Users definitely expect Ctrl+Z to work here

#### 1. Insert/Remove Keyframe via UI buttons (no undo) -- **DONE (verified 2026-04-22)**

**Files:**
- `Editor/UiModel/InputsAndTypes/InputValueUi.cs:343-350` -- stale TODO removed
- `Editor/UiModel/InputsAndTypes/InputValueUi.cs:380,389` -- same pattern
- `Editor/Gui/Windows/TimeLine/DopeSheetArea.cs` -- `InsertNewKeyframe()` routes through `AnimationOperations`

**Status:** `AnimationOperations.InsertKeyframeToCurves` / `RemoveKeyframeFromCurves` already wrap each operation in `AddKeyframesCommand` / `DeleteKeyframeCommand` and push a `MacroCommand` to `UndoRedoStack` ([AnimationOperations.cs:36-37](Editor/Gui/Interaction/Animation/AnimationOperations.cs:36), [AnimationOperations.cs:54](Editor/Gui/Interaction/Animation/AnimationOperations.cs:54)). All call sites listed above (parameter-row keyframe toggle, context menu insert/remove, dope-sheet shortcut) flow through these helpers. Ctrl+Z works.

---

#### 2. Duplicate Keyframes in Timeline (no undo) -- **DONE (verified 2026-04-22)**

**Files:**
- `Editor/Gui/Windows/TimeLine/DopeSheetArea.cs:57-60`
- `Editor/Gui/Windows/TimeLine/TimelineCurveEditor.cs:69-70`

**Status:** `DuplicateSelectedKeyframes()` → `CopySelectedKeyframes()` (clipboard only) + `PasteKeyframes()`, and `KeyframeCopyAndPasting.TryPasteTo` wraps all inserts in a `MacroCommand("Paste keyframes", ...)` pushed via `UndoRedoStack.AddAndExecute` ([KeyframeCopyAndPasting.cs:130](Editor/Gui/Windows/TimeLine/KeyframeCopyAndPasting.cs:130)). Ctrl+D is undoable.

---

#### 3. Delete Keyframes in Timeline (no undo) -- **DONE (verified 2026-04-22)**

**Files:**
- `Editor/Gui/Windows/TimeLine/DopeSheetArea.cs:1085-1092` -- `DeleteSelectedElements()`
- `Editor/Gui/Windows/TimeLine/TimelineCurveEditor.cs:469-473` -- same

**Status:** Both call sites route through `AnimationOperations.DeleteSelectedKeyframesFromAnimationParameters`, which builds a list of `DeleteKeyframeCommand` / `RemoveAnimationsCommand` and pushes via `UndoRedoStack.AddAndExecute(new MacroCommand("Delete keyframes", commands))` ([AnimationOperations.cs:97](Editor/Gui/Interaction/Animation/AnimationOperations.cs:97)). Delete key is undoable.

---

#### 4. Insert Keyframe with Increment (no undo) -- **DONE (verified 2026-04-22)**

**File:** `Editor/Gui/Windows/TimeLine/DopeSheetArea.cs:72-80`

**Status:** Calls `InsertNewKeyframe(p, time, false, 1)` → `AnimationOperations.InsertKeyframeToCurves(curves, time, increment: 1)`, same command-wrapped path as #1.

---

#### 5. Set Current Value as Default -- **DONE (2026-04-22)**

**Files touched:**
- New [SetInputDefaultCommand.cs](Editor/UiModel/Commands/Graph/SetInputDefaultCommand.cs)
- [InputValueUi.cs:245](Editor/UiModel/InputsAndTypes/InputValueUi.cs:245), [InputValueUi.cs:470](Editor/UiModel/InputsAndTypes/InputValueUi.cs:470) -- both context menus now push the command
- [Symbol.cs](Core/Operator/Symbol.cs) -- added `InvalidateInputDefaultInInstances(in Guid)` overload so the command can operate without an `IInputSlot`

**Note:** `GradientInputUi.cs:35` calls `SetCurrentValueAsDefault()` only in a defensive path that reconstructs a null gradient — not a user-initiated action. Left untouched.

---

#### 6. Variation Create/Update/Remove -- **MOSTLY DONE (2026-04-22)**

**Files touched:**
- New [UpdateVariationParametersCommand.cs](Editor/UiModel/Commands/Variations/UpdateVariationParametersCommand.cs) -- deep-clones the ParameterSetsForChildIds dict before/after; used by `UpdateVariationPropertiesForInstances`
- New [RemoveInstancesFromVariationsCommand.cs](Editor/UiModel/Commands/Variations/RemoveInstancesFromVariationsCommand.cs) -- captures removed entries so they can be restored on undo; used by `VariationHandling.RemoveInstancesFromVariations`
- Both callers now accept a `collectInto: MacroCommand?` parameter so they can be grouped with a `ChangeSnapshotEnabledCommand` (#11) instead of producing separate undo entries.

**Residual:** `CreateOrUpdateSnapshotVariation` still produces two undo entries when replacing an existing snapshot (one for `DeleteVariation`, one for `TryCreateVariationForCompositionInstances`). Non-critical — the user just hits Ctrl+Z twice. Fixing needs either a MacroCommand wrapper or non-pushing helper variants; left as follow-up.

**Persistence note:** The new commands call `pool.SaveVariationsToFile()` on both Do and Undo so the file matches the in-memory state. The pre-existing `AddPresetOrVariationCommand` / `DeleteVariationCommand` do *not* save on Do/Undo — that's a pre-existing inconsistency worth cleaning up but out of scope here.

---

#### 7. Curve Tangent Handle Editing -- **DONE (verified 2026-04-22)**

**File:** `Editor/Gui/Interaction/WithCurves/CurvePoint.cs`

**Status:** Closed by commit `d890a482c` ("ui: modifying curve tangents can be undone"). In-flight `ChangeKeyframesCommand` is created on `ImGui.IsItemActivated` ([CurvePoint.cs:107-112](Editor/Gui/Interaction/WithCurves/CurvePoint.cs:107)) and committed via `StoreCurrentValues()` + `UndoRedoStack.Add` on `IsItemDeactivated` ([CurvePoint.cs:149-154](Editor/Gui/Interaction/WithCurves/CurvePoint.cs:149)). Menu-driven interpolation changes (Smooth/Cubic/Horizontal/Constant/Linear/MirrorTangents) route through `ForSelectedOrAllPointsDo` which wraps mutations in a `ChangeKeyframesCommand` ([CurveEditing.cs:383-395](Editor/Gui/Interaction/WithCurves/CurveEditing.cs:383)).

---

#### 8. Parameter Extraction -- **DONE (2026-04-22)**

**Files touched:**
- New [SetExtractedInputValuesCommand.cs](Editor/UiModel/Commands/Graph/SetExtractedInputValuesCommand.cs) -- restores extracted input values on redo, resets them on undo
- [ParameterExtraction.cs](Editor/Gui/Graph/Interaction/ParameterExtraction.cs) -- accepts an optional `collectInto: MacroCommand` so it can merge into an outer macro; the previous `ExtractInputValues` step is now part of the macro
- [MagGraphView.cs:201](Editor/Gui/MagGraph/Ui/MagGraphView.cs:201) -- passes the context's macro in via `collectInto`, so "Extract parameters" is one undo entry instead of two nested ones

---

#### 9. Edit Symbol Description / Links -- **DONE (2026-04-22)**

**Files touched:**
- New [ChangeSymbolDescriptionCommand.cs](Editor/UiModel/Commands/Graph/ChangeSymbolDescriptionCommand.cs) -- stores before/after description and deep-cloned link list; Do/Undo clears and repopulates `Links`
- [EditSymbolDescriptionDialog.cs](Editor/Gui/Graph/Dialogs/EditSymbolDescriptionDialog.cs) -- snapshots on dialog open, commits one command on close (including ESC / click-outside), uses `UndoRedoStack.Add` (not `AddAndExecute`) because the live model is already at the new state

---

#### 10. Edit Node Comment (no undo) -- **DONE (2026-04-22)**

**File:** `Editor/Gui/Graph/Dialogs/EditCommentDialog.cs`

**Resolution:** Added [ChangeCommentCommand.cs](Editor/UiModel/Commands/Graph/ChangeCommentCommand.cs) (stores original+new comment, looks up child by Guid via `SymbolUiRegistry`). Dialog now buffers edits locally and commits a single `ChangeCommentCommand` via `UndoRedoStack.AddAndExecute` when closed (button, ESC, or click-outside).

---

#### 11. Snapshot Enable/Disable Toggle -- **DONE (2026-04-22)**

**Files touched:**
- New [ChangeSnapshotEnabledCommand.cs](Editor/UiModel/Commands/Graph/ChangeSnapshotEnabledCommand.cs) -- stores each child's pre-change `SnapshotGroupIndex` so undo restores the exact previous state
- Both context menus (MagGraph `GraphContextMenu.cs` and legacy `GraphView.cs`) now build a `MacroCommand("Toggle snapshot enabled")` containing the toggle command plus any `RemoveInstancesFromVariations` / `UpdateVariationPropertiesForInstances` calls via the new `collectInto` plumbing from #6. One undo entry, not three.

---

#### 12. Playback Settings Changes (no undo)

**Files:**
- `Editor/Gui/Windows/TimeLine/PlaybackSettingsPopup.cs:271`
- `Editor/Gui/Windows/TimeLine/TimeControls.cs:197,391`

**What happens:** Changing BPM, sync mode, and other playback settings directly mutates `PlaybackSettings` and calls `FlagAsModified()`.

**Fix:** Create `ChangePlaybackSettingsCommand` or consider these "project settings" that don't need undo. This is debatable -- BPM changes are often experimental and users may want to undo them.

**Effort:** LOW-MEDIUM (half day if deemed necessary)

---

#### 13. StructuredList Input Editing -- **DEFERRED (notes 2026-04-22)**

**File:** [StructuredListInputUi.cs:35](Editor/Gui/InputUi/CombinedInputs/StructuredListInputUi.cs:35)

**Why not done:** The generic `ChangeInputValueCommand` pipeline in [ParameterWindow.cs:579-607](Editor/Gui/Windows/ParameterWindow.cs:579) handles most parameter types for free — it constructs a command on `InputEditStateFlags.Started`, accumulates on `Modified`, and pushes on `Finished`. Making StructuredList ride that pipeline requires two non-trivial changes:

1. **`StructuredList` must implement `IEditableInputType`** (trivial code change — `Clone()` already exists and deep-copies the backing array) so that `InputValue<StructuredList>.Clone()` actually snapshots state. Currently the non-`IEditableInputType` path does a shallow clone and the "original" captured by `ChangeInputValueCommand` aliases the live list.

2. **`StructuredListInputUi.DrawEditor` must emit `Started` before any mutation** and `Finished` when editing ends. `TableList.Draw` both renders *and* mutates in one call, so a naive "check `ImGui.IsAnyItemActive()` after" captures state after mutation. Needs either (a) split TableList into check-before + apply, or (b) snapshot the list into a thread-local buffer before `TableList.Draw` and expose it to the pipeline.

Tractable but a separate session. Low user-impact in practice — structured list inputs are rare.

---

#### 14. Split Clip at Time -- **DONE (2026-04-22)**

**File:** [LayersArea.cs](Editor/Gui/Windows/TimeLine/TimeClips/LayersArea.cs)

**Resolution:** The new clip's TimeRange/SourceRange mutation (previously direct) is now wrapped in its own `MoveTimeClipsCommand` — constructed before mutation, `StoreCurrentValues()` called after. On undo the macro reverses in order: remove connections → revert old-clip time → revert new-clip time → rename back → delete new child. On redo it reapplies. The "incomplete and likely to lead to inconsistent data" remark was removed since the macro is now complete.

---

#### 15. Tour Point Editing (no undo)

**Files:**
- `Editor/Gui/Graph/Dialogs/EditTourPointsPopup.cs:166`
- `Editor/Gui/Graph/Dialogs/TourDataMarkdownExport.cs:241,245,319`

**What happens:** Creating/editing tour points and markdown export directly mutates `SymbolUi.Description` and tour point properties.

**Fix:** Lower priority -- tour editing is an authoring tool, not a frequent undo target. Could wrap in command if desired.

**Effort:** LOW (but low priority)

---

### LOWER PRIORITY -- Internal or edge-case mutations

#### 16. NodeActions.cs -- Command executed without UndoRedoStack -- **DONE (2026-04-22)**

**File:** `Editor/UiModel/Modification/NodeActions.cs:236`

**Resolution:** Replaced `cmd.Do();` with `UndoRedoStack.AddAndExecute(cmd);`. The subsequent reads of `cmd.NewSymbolChildIds` / `NewSymbolAnnotationIds` still work because `AddAndExecute` runs `Do()` synchronously before returning.

---

#### 17. Auto-Layout / RecursivelyAlignChildren (no undo)

**File:** `Editor/Gui/Graph/Legacy/Interaction/NodeGraphLayouting.cs:58-60`

**What happens:** Auto-layout directly sets `childUi.PosOnCanvas` for all children.

**Fix:** Wrap in `ModifyCanvasElementsCommand`. Need to capture all positions before layout, apply layout, then store in command.

**Effort:** MEDIUM (half day)

---

#### 18. MagGraphLayout FlagAsModified without command

**File:** `Editor/Gui/MagGraph/Model/MagGraphLayout.cs:55`

**What happens:** `FlagAsModified()` called during layout size adjustments. These are internal layout recalculations, not user-initiated.

**Fix:** Likely not needed -- this is view-layer computation, not model mutation.

**Effort:** N/A

---

#### 19. FloatVectorInputValueUi direct FlagAsModified

**File:** `Editor/Gui/InputUi/VectorInputs/FloatVectorInputValueUi.cs:179`

**What happens:** `Parent?.FlagAsModified()` called from input UI code. Need to verify if this is inside a command flow or not.

**Fix:** Investigate -- may be a false positive if called within `ChangeInputValueCommand` flow.

**Effort:** INVESTIGATE (1 hour)

---

#### 20. SymbolLibrary namespace change

**File:** `Editor/Gui/Windows/SymbolLib/SymbolLibrary.cs:698`

**What happens:** `FlagAsModified()` called. Verify if wrapped in `ChangeSymbolNamespaceCommand`.

**Fix:** Investigate.

**Effort:** INVESTIGATE (1 hour)

---

## Summary Table

| # | Gap | Severity | Effort | Status |
|---|-----|----------|--------|--------|
| 1 | Insert/Remove Keyframe via UI | CRITICAL | LOW | DONE |
| 2 | Duplicate Keyframes | CRITICAL | LOW-MED | DONE |
| 3 | Delete Keyframes in Timeline | CRITICAL | LOW | DONE |
| 4 | Insert Keyframe with Increment | CRITICAL | LOW | DONE |
| 5 | Set Value as Default | CRITICAL | MEDIUM | DONE |
| 6 | Variation CRUD | CRITICAL | MED-HIGH | mostly DONE (CreateOrUpdate remains 2-step) |
| 7 | Curve Tangent Editing | CRITICAL | MEDIUM | DONE |
| 8 | Parameter Extraction | CRITICAL | MEDIUM | DONE |
| 9 | Edit Symbol Description/Links | MEDIUM | LOW-MED | DONE |
| 10 | Edit Node Comment | MEDIUM | LOW | DONE |
| 11 | Snapshot Enable Toggle | MEDIUM | MEDIUM | DONE |
| 12 | Playback Settings | LOW-MED | LOW-MED | open |
| 13 | StructuredList Editing | MEDIUM | MEDIUM | deferred (see notes) |
| 14 | Split Clip at Time | MEDIUM | MEDIUM | DONE |
| 15 | Tour Point Editing | LOW | LOW | open |
| 16 | NodeActions cmd.Do() bypass | LOW | TRIVIAL | DONE |
| 17 | Auto-Layout positions | LOW | MEDIUM | open |
| 18 | MagGraphLayout internal | N/A | N/A | not a gap |
| 19 | FloatVectorInput (investigate) | ? | INVESTIGATE | open |
| 20 | SymbolLibrary (investigate) | ? | INVESTIGATE | open |

## Recommended Implementation Order

**Batch 1 -- Quick wins, highest user impact (2-3 days): DONE (2026-04-22)**
- #1, #2, #3, #4 -- verified already covered by `AnimationOperations` / `KeyframeCopyAndPasting`
- #10 -- new `ChangeCommentCommand`, dialog reworked to commit on close
- #16 -- `cmd.Do()` replaced with `UndoRedoStack.AddAndExecute(cmd)`

**Batch 2 -- Important gaps (2-3 days): DONE (2026-04-22)**
- #5 -- new `SetInputDefaultCommand`
- #7 -- verified already covered by commit `d890a482c`
- #8 -- new `SetExtractedInputValuesCommand` + `collectInto` macro plumbing
- #9 -- new `ChangeSymbolDescriptionCommand`

**Batch 3 -- Complex features (3-5 days): MOSTLY DONE (2026-04-22)**
- #6 -- new `UpdateVariationParametersCommand`, `RemoveInstancesFromVariationsCommand`, `collectInto` plumbing; CreateOrUpdateSnapshotVariation 2-step issue remains
- #11 -- new `ChangeSnapshotEnabledCommand`, single undo entry for toggle + variation mutations
- #13 -- deferred, see notes
- #14 -- new-clip TimeRange mutation now wrapped in its own `MoveTimeClipsCommand`

**Batch 4 -- Lower priority (as needed):**
- #12, #15, #17, #19, #20

## Testing Strategy

Each undo/redo fix should include a corresponding integration test in the command test project (see AUTOMATIC_TEST_PLAN.md Phase 1):

```csharp
[Fact]
public void InsertKeyframe_Undo_RemovesKeyframe()
{
    // Arrange: create symbol with animated input
    // Act: execute InsertKeyframeCommand
    // Assert: keyframe exists at time T
    // Act: undo
    // Assert: keyframe no longer exists at time T
}
```

This ensures that fixing undo/redo gaps doesn't introduce regressions, and that the fixes remain stable as the codebase evolves.
