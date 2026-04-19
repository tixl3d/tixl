---
id: dopesheet-curve-expand
title: DopeSheet Per-Parameter Curve Expand
scope: timeline
tags: [essential]
prerequisites:
  - A project with at least one animated float parameter and one animated Vector3 parameter is open
  - The Timeline window is visible in DopeView mode
related-help: []
---

Verifies the per-parameter inline curve editor: expand toggle, component visibility, selection sync between dope sheet and curve view, splitter persistence, and the Normalize view. See [`Plan_DopeSheetCurveExpand.md`](../.agentic/Plans/Plan_DopeSheetCurveExpand.md) for the feature design.

## Step: Expand a float parameter

**Context:** Timeline in DopeView. No parameters expanded.
**Action:**
- Click the curve-expand icon on an animated float parameter's row

**Expected:**
- The timeline body splits vertically: dope sheet on top, curve editor below.
- The expanded parameter's curve is rendered in the new pane.

## Step: Collapse the float parameter

**Context:** One float parameter is expanded.
**Action:**
- Click the curve-expand icon again on the same row

**Expected:**
- The curve editor pane disappears.
- The dope sheet reclaims the full height.

## Step: Expand a Vector3 parameter

**Context:** Timeline in DopeView.
**Action:**
- Click the curve-expand icon on an animated Vector3 parameter's row

**Expected:**
- Three component toggle buttons (`.x .y .z`) appear on that parameter's dope-sheet row, after the name.
- All three curves render in the curve pane.

## Step: Hover a component toggle

**Context:** A Vector3 parameter is expanded.
**Action:**
- Hover the `.y` component toggle

**Expected:**
- The `.x` and `.z` curves fade to reduced opacity in both the dope layer and the curve pane.
- The `.y` curve remains at full opacity.

## Step: First click on a component isolates it

**Context:** A Vector3 parameter is expanded, all three components visible (default state).
**Action:**
- Click the `.y` component toggle

**Expected:**
- Only the `.y` curve continues to render in both the dope layer and the curve pane.
- `.x` and `.z` curves disappear; their toggle letters dim.

## Step: Subsequent clicks add or remove components

**Context:** Continued — only `.y` is visible.
**Action:**
- Click the `.z` component toggle (add)

**Expected:**
- Both `.y` and `.z` are now visible; `.x` still hidden.

**Action:**
- Click the `.y` component toggle (remove)

**Expected:**
- Only `.z` is visible.

## Step: Clicking the last visible component restores default

**Context:** Continued — only `.z` visible.
**Action:**
- Click the `.z` component toggle

**Expected:**
- All three components are visible again (mask entry dropped back to default).

## Step: Hover a dope-sheet layer highlights its curves

**Context:** At least two parameters are expanded so multiple curves are visible in the curve pane.
**Action:**
- Move the mouse over the background of one expanded parameter's dope-sheet row (not over an icon, button, or keyframe)

**Expected:**
- The dope-sheet row gets the hovered background tint.
- In the curve pane, this parameter's curves stay at full opacity; other parameters' curves fade.

## Step: Hover a curve line highlights its dope-sheet layer

**Context:** At least two parameters are expanded, curves visible.
**Action:**
- Move the mouse over a curve line segment in the curve pane (not over a keyframe)

**Expected:**
- The dope-sheet row for the curve's parameter gets the hovered background tint.
- Other parameters' curves in the curve pane fade.

## Step: Hover a keyframe highlights it in both views

**Context:** A parameter is expanded. Curve pane and dope-sheet row are both visible for it.
**Action:**
- Hover a keyframe icon in the curve pane

**Expected:**
- The hovered keyframe's icon is outlined.
- The matching keyframe in the dope-sheet row (same U) is outlined with the same treatment.

Then reverse:
- Hover a keyframe in the dope-sheet row

**Expected:**
- All matching per-component keyframes at that U in the curve pane show the same outline.

## Step: Component state resets on re-expand

**Context:** A Vector3 parameter is expanded with `.x` hidden.
**Action:**
- Click the parameter's curve-expand icon (collapse)
- Click it again (re-expand)

**Expected:**
- All three components are visible again — the previous hide is forgotten.

## Step: Parameter name click selects keyframes

**Context:** At least one parameter has no keyframes currently selected.
**Action:**
- Click the parameter's name button (not the pin or curve icons)

**Expected:**
- The keyframe selection is replaced with that parameter's keyframes.

## Step: Shift+click adds to selection

**Context:** Some keyframes are selected.
**Action:**
- Shift+click a different parameter's name button

**Expected:**
- That parameter's keyframes are added to the existing selection.

## Step: Ctrl+click removes from selection

**Context:** A parameter's keyframes are currently part of the selection.
**Action:**
- Ctrl+click the same parameter's name button

**Expected:**
- That parameter's keyframes are removed from the selection.

## Step: Single-component selection reflects in dope sheet

**Context:** A Vector3 parameter is expanded.
**Action:**
- In the curve pane, click one `.y` keyframe to select it alone

**Expected:**
- The corresponding dope-sheet row shows the stacked keyframe at that U as selected.

## Step: Curve area auto-grows when few DSA layers

**Context:** Timeline shows only one or two animated parameters and one is expanded.
**Action:**
- Observe the pane split

**Expected:**
- Curve area takes the majority of the timeline height; dope sheet is only as tall as it needs (params × layer height). Not stuck at 50/50.

## Step: Tall curve area centers instead of over-stretching

**Context:** Same as above — curve area is clearly taller than half of the timeline.
**Action:**
- Observe the curves' vertical fit

**Expected:**
- Curves don't stretch to fill the whole curve-area height; they sit at the "reference" (50%-height) scale with padding above/below. Visual scale remains reasonable.

## Step: Keyframe drag axis latches (U-only or V-only)

**Context:** A parameter is expanded with a selected keyframe in the curve area.
**Action:**
- Press the keyframe and slowly drag — first, drag mostly horizontally
- Release
- Press again, drag mostly vertically

**Expected:**
- The first drag only moves the keyframe in time (U); no V change.
- The second drag only moves the keyframe in value (V); no U change.
- The cursor changes to a horizontal ↔ / vertical ↕ arrow once the latch engages.
- On release the latch resets (next drag starts undecided again).

## Step: Tangent edit is undoable

**Context:** Selected keyframe in the curve area.
**Action:**
- Drag a tangent handle to a clearly different angle
- Release
- Press `Ctrl+Z`

**Expected:**
- The tangent returns to its pre-drag angle/tension.
- `Ctrl+Shift+Z` (redo) restores the edit.

## Step: Curve area auto-closes when selection changes

**Context:** A parameter is expanded; switch the graph selection to a different operator whose animated parameters don't overlap with the current expanded set.
**Action:**
- Select an unrelated operator in the graph

**Expected:**
- The curve area disappears (no longer any valid expanded params).
- Re-selecting the original operator does NOT re-expand the parameter — user must click the curve-toggle again.

## Phase 4 / 5 scenarios (not yet implemented — pending follow-up PRs)

The steps below describe planned behavior. Skip until the relevant phase ships — they're documented here so the test set doesn't have to be rewritten later.

### (deferred) Step: Drag splitter

Drag a 3 px handle between dope sheet and curve area to override the auto-split ratio. Clamped to approximately [0.15, 0.85]. Persisted via `UserSettings`.

### (deferred) Step: Normalize view flattens magnitudes

Toggling the normalize button maps each curve to `[-1, 1]` by its own value range. Curves of very different magnitudes become visually comparable. V-drag and tangent edits continue to work on real values under the hood; the flat-curve edge case (single keyframe) uses an ephemeral range so the key can leave the zero line.

## Step: Close button collapses all

**Context:** One or more parameters are expanded.
**Action:**
- Click the Close icon in the curve pane's floating chrome

**Expected:**
- The curve pane disappears.
- All affected parameters' curve-expand toggles return to off.
- Component visibility state is cleared.
