# Timeline Selection UI — Selection Range Indicator & Selection Area

**Date:** 2026-04-17 (plan written & mostly implemented same day)
**Source brief:** `D:\Framefield Dropbox\Thomas Mann\_projects\2026-04-14 TiXL Ideas\Timeline ui implementation.md`
**Reference design:** Figma node `1464:2659` (Timeline frame) and `1464:2562` (full-window context)

## Summary

Two new UI components in the timeline:

1. **Selection Range Indicator (SRI)** — thin horizontal bar with start/end handles inside the ruler, spanning the time range of the current keyframe selection. Drag edges to stretch, middle to translate.
2. **Selection Area (SA)** — shallow strip directly below the ruler showing a baked icon per keyframe cluster. Click replaces the keyframe selection with the cluster's keys; drag moves just that cluster's keys without touching the broader selection.

The brief lists much more (popups, Alt-remap, fence selection, time markers, detached window, dope-sheet sorting). This plan **scoped iteration 1 to the visuals + core interactions**; the rest is in "Remaining work" below.

## What was already in the codebase

- `TimeLineCanvas.cs` — ruler via `_timeRasterSwitcher` inside a `##ruler` child, followed by a shaded 9-px `##summary` strip (now repurposed as the SA).
- `TimeSelectionRange.cs` — legacy component visible only while Alt is held. Draws canvas-wide shading and bottom handles; handles stretch-drag. **Kept untouched** — SRI is additive, not a replacement.
- `AnimationCanvas.StartDragCommand` / `UpdateDragCommand` / `UpdateDragStretchCommand` / `CompleteDragCommand` — existing drag plumbing, used by the SRI. (SA uses its own command, see below.)
- `DopeSheetArea.SelectedKeyframes` (inherited from `CurveEditing`) — `HashSet<VDefinition>` of the selection. Now wrapped (see `VersionedKeyframeSet` below).
- `Curve.ChangeCount` — per-curve monotonic counter for change detection.
- `VDefinition.UniqueId` — stable atomic-int identity per keyframe (survives value/U mutations).

## Shipped architecture

### `SelectionRangeIndicator.cs` (new)

- Reads `canvas.GetSelectionTimeRange()` each frame. Early-out if `!IsValid` or `Duration <= 0` (so a single-keyframe selection shows nothing).
- Inside the `##ruler` child: horizontal 1-px line between the start/end screen-X, clipped to the ruler rect; two 5-px square handles at each end.
- Hit-test: three invisible buttons (start handle, end handle, middle region). Middle is emitted first so edge handles win on overlap.
- Edge drag: `StartDragCommand` → `UpdateDragStretchCommand(dScale, 1, origin, 0)` → `CompleteDragCommand`, same pattern as `TimeSelectionRange.HandleDrag`. Uses `canvas.SnapHandlerForU` with Shift to bypass.
- Middle drag: `StartDragCommand` → `UpdateDragCommand(du, 0)` → `CompleteDragCommand` for translation.
- Registered as an `IValueSnapAttractor` (start + end U values). Excludes itself from its own snap check via `_snapExclusions`.

### `TimeSelectionArea.cs` (new)

- Replaces the body of the `##selectionArea` child (renamed from `##summary`). Strip height bumped from 9 → 11 px × `T3Ui.UiScaleFactor`.
- **Rendering:** baked icons from the font atlas, one per bucket, via `Icons.DrawIconAtScreenPosition(icon, pos, drawList, color)`:
  - `Icon.KeyIndicator` — unselected
  - `Icon.KeyIndicatorSelectedPartially` — some selected
  - `Icon.KeyIndicatorSelected` — all selected
  Default tint `UiColors.ForegroundFull.Fade(0.7f)`; hovered/active bucket gets full opacity. All icons share the atlas texture and batch into one draw call.
- **Bucketing:** walk every visible keyframe, compute screen-X via `canvas.TransformX(u)`, sort by X, group any run whose bounding width stays within **2 px × `T3Ui.UiScaleFactor`**. This is a float-precision guard — at 2 px the user can't visually resolve pills anyway.
- **Caching:** `RebuildBuckets` only runs when a composite state hash changes. Inputs to the hash: `Scale.X`, `Scroll.X`, window rect, parameter count, per-parameter `Hash`, per-curve `ChangeCount`, `DopeSheetArea.SelectionChangeCounter`. No per-frame allocations in steady state; rebuild reuses static `List<RawKey>` / `List<Bucket>` buffers.
- **Interaction — one strip-wide `##saStrip` InvisibleButton** (not per-bucket). Stable ID means ImGui's active-item tracking persists across bucket rebuilds, which is what lets a drag cross another cluster without getting cancelled. Manual hit-test against cached bucket centers determines which bucket is pressed/hovered.
  - **Click (no drag past 2 px):** on `IsItemDeactivated` with `!_isDragging`, call `DopeSheetArea.ReplaceKeyframeSelection(bucket's keys)`.
  - **Drag:** on first `IsMouseDragging(2 px)` after press, capture `_draggingKeys` + `_draggingCurves` from the pressed bucket, construct a private `ChangeKeyframesCommand`, record `_dragGrabOffset = mouseU_start - _draggingKeys[0].U`. Each frame: `desiredAnchorU = mouseU - _dragGrabOffset` → snap → `du = desiredAnchorU - _draggingKeys[0].U` → `DopeSheetArea.ApplyKeyframeTimeOffset(_draggingKeys, du)`. On release: `StoreCurrentValues()` + `UndoRedoStack.AddAndExecute`. The existing `SelectedKeyframes` is never touched during drag.
  - **Bucket identity across frames:** each bucket's `StableId` is the `VDefinition.UniqueId` of its leftmost keyframe. `FindBucketByStableId` first tries a direct match, then falls back to scanning `_rawKeys` for the anchor VDefinition and resolving its enclosing bucket — so the dragged bucket stays identifiable even when it transiently merges into or splits out of a neighbour cluster.

### `VersionedKeyframeSet.cs` (new, under `Editor/Gui/Interaction/WithCurves/`)

- `HashSet<VDefinition>`-shaped wrapper that bumps a public `ChangeCounter` on every mutation (`Add`/`Remove`/`Clear`/`UnionWith`/`ExceptWith`).
- `CurveEditing.SelectedKeyframes` is now typed as `VersionedKeyframeSet` — all derived classes (DopeSheetArea, TimelineCurveEditArea, AnimationParameterEditing, CurveInteraction) pick up the new type without code changes at mutation sites.
- Implements `IEnumerable<VDefinition>` for compatibility with `ChangeKeyframesCommand(IEnumerable<VDefinition>, ...)` and `DeleteSelectedKeyframesFromAnimationParameters(...)`. Per-frame `foreach` uses the struct enumerator (zero alloc); the interface path boxes only on rare events (drag-start, delete).

### `DopeSheetArea.cs` — new public helpers

- `IsKeyframeSelected(VDefinition) → bool`
- `SelectionChangeCounter → int` (forwards `SelectedKeyframes.ChangeCounter`)
- `ReplaceKeyframeSelection(IEnumerable<VDefinition>)`
- `ApplyKeyframeTimeOffset(IReadOnlyList<VDefinition>, double deltaU)` — shifts key U values and calls `RebuildCurveTables()`. Caller wraps it in a `ChangeKeyframesCommand` for undo.

### Icons baked into the atlas (user added)

`Editor/Gui/Styling/Icons.cs` + `EditorResources/images/t3-icons*.png`:

- `Icon.KeyIndicator`
- `Icon.KeyIndicatorSelected`
- `Icon.KeyIndicatorSelectedPartially`

All 7×7, drawn via `Icons.DrawIconAtScreenPosition`, share the atlas.

## Decisions (resolved during implementation)

- **SRI hidden when `Duration == 0`** — single-keyframe selections don't draw the SRI at all.
- **SA merge threshold = 2 px × `T3Ui.UiScaleFactor`** (bounding-width, not gap-based). Pure float-precision guard.
- **SA interaction split:** click = replace selection; drag = move cluster keys, selection untouched. The SRI handles "drag the whole selection".
- **Caching keyed on state hash** of `Scale.X`, `Scroll.X`, window rect, `AnimationParameter.Hash`, `Curve.ChangeCount`, `SelectionChangeCounter`. Rebuild is skipped when nothing relevant changed.

## Bugs caught during implementation (documented so we don't re-learn them)

- **SelectionFence ate the drag selection.** `AnimationCanvas`'s `SelectionFence` uses `ImGui.IsWindowHovered(AllowWhenBlockedByPopup | ChildWindows)`. Because the `##selectionArea` child is nested under the main canvas child, a press there looked like "empty canvas click" to the fence unless an item was hovered. Emitting an `InvisibleButton` on the press frame (`##saStrip`) sets `IsAnyItemHovered() = true` and the fence early-exits. Subsequent drag frames keep the fence inactive.
- **Per-bucket buttons broke cross-cluster drags.** The first implementation used one `InvisibleButton` per bucket keyed by `PushID(stableId)`. When the dragged bucket merged into another cluster mid-drag, that `stableId` stopped being emitted → ImGui reported the item deactivated → drag cancelled. Fix: single strip-wide button with a constant ID.
- **Snap was off by 1–4 px at high zoom.** Delta-accumulation (`du = snappedMouseU - _lastDragU`) lets the grab offset between mouse and key leak through, so keys land at `(grabOffset)` past the snap target. Dope sheet doesn't hit this because it clicks *on* the key (grab offset ≈ 0). Fix: capture `_dragGrabOffset` at drag start, then each frame snap `desiredAnchorU = mouseU - _dragGrabOffset` and compute `du = desiredAnchorU - _draggingKeys[0].U` — the anchor key's target U is what snaps, not the mouse.

## Visual specifications (from Figma metadata, for reference)

Ruler frame (`1464:2672`, 909×22 in Figma):
- SRI at y≈14.5 within the 22 px ruler, height≈5.5.
- Start/End handles: 5×5 vectors; rangeLine 1 px tall between them.

Selection Area (`1464:2686`, 909×7):
- Keymarker instances: 7×7 px each. Figma shows them as distinct pills even when adjacent — we merge at ≤ 2 px for the float-precision reasons above.

## Files touched

- **New:**
  - `Editor/Gui/Windows/TimeLine/SelectionRangeIndicator.cs`
  - `Editor/Gui/Windows/TimeLine/TimeSelectionArea.cs`
  - `Editor/Gui/Interaction/WithCurves/VersionedKeyframeSet.cs`
- **Modified:**
  - `Editor/Gui/Windows/TimeLine/TimeLineCanvas.cs` — construct both components, register SRI as snap attractor, wire `DrawCanvasContent` (SRI inside ruler child, SA in the summary-turned-`##selectionArea` child), bumped `SummaryHeight` 9 → 11.
  - `Editor/Gui/Windows/TimeLine/DopeSheetArea.cs` — added the four public helpers listed above.
  - `Editor/Gui/Interaction/WithCurves/CurveEditing.cs` — typed `SelectedKeyframes` as `VersionedKeyframeSet`.
  - `Editor/Gui/Interaction/Animation/AnimationOperations.cs` — signature: `HashSet<VDefinition>` → `VersionedKeyframeSet`.
  - `Editor/Gui/Styling/Icons.cs` + `EditorResources/images/t3-icons*.png` — new KeyIndicator glyphs (user-authored).
  - `.agentic/AGENT_INSTRUCTIONS.md` — expanded UI guidelines (separate concern, not part of this feature).

## Remaining work (follow-ups from the source brief)

- **Ruler:** click SRI without drag → "modify selection" popup (numeric duration, quantize, interpolation type); Alt-drag SRI → remap mode; hover tooltip showing time.
- **SA:** click with Shift to add to selection (instead of replace); fence-select over SA background with Shift/Ctrl modifiers; click on a bucket to open a manipulation popup.
- **Time markers** — not yet started. Separate feature: markers saved with symbol animation, ctrl+number to jump, tag-based todo workflow, etc.
- **Detached timeline window** — layout-level change, separate plan.
- **Dope-sheet enhancements** — parameter sorting by canvas position, stack indicator for near-coincident keyframes.
- **Identity stability across undo/redo clone cycles** — latent concern. Migrating `SelectedKeyframes` from `HashSet<VDefinition>` (reference identity) to `HashSet<int>` (UniqueId) would make the selection survive deep-clone undo. Explicitly deferred; worth its own plan.
