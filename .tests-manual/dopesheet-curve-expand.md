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

## Step: Toggle a component off

**Context:** A Vector3 parameter is expanded, all three components visible.
**Action:**
- Click the `.x` component toggle

**Expected:**
- The `.x` curve stops rendering in both the dope layer and the curve pane.
- The `.x` toggle shows a visibly inactive state.

## Step: Toggle the hidden component back on

**Context:** Continued from previous step.
**Action:**
- Click the `.x` component toggle again

**Expected:**
- The `.x` curve reappears in both views.

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

## Step: Drag splitter

**Context:** Curve pane is open.
**Action:**
- Drag the splitter handle between dope sheet and curve pane upward, then downward

**Expected:**
- The two panes resize smoothly.
- The split ratio is clamped (handle cannot pass very close to either edge).

## Step: Splitter ratio persists across restart

**Context:** Curve pane is open with splitter at a non-default position.
**Action:**
- Close and reopen the editor
- Reopen the same project
- Expand any parameter

**Expected:**
- The splitter appears at the ratio from before the restart.

## Step: Normalize view flattens magnitudes

**Context:** At least two expanded parameters with very different value magnitudes (e.g. one in `[0, 1]`, one in `[0, 1000]`).
**Action:**
- Click the Normalize toggle in the curve pane's floating chrome

**Expected:**
- Both curves now fit inside a `[-1, 1]` vertical range.
- Each curve retains its relative shape.
- Keyframes sit on their respective curves.

## Step: Vertical drag under Normalize (real-V scaling)

**Context:** Normalize on. Two curves with very different magnitudes are visible.
**Action:**
- Drag a keyframe on the large-magnitude curve vertically by a small amount

**Expected:**
- The keyframe's real value changes by the scaled amount (not the raw screen-pixel amount).
- Keyframes on other curves are unaffected.
- Toggling Normalize off shows the change at the correct real-V magnitude.

## Step: Tangent edit under Normalize

**Context:** Normalize on.
**Action:**
- Grab a tangent handle and edit the slope

**Expected:**
- The on-screen slope change is intuitive (matches visual drag).
- Toggling Normalize off shows a correctly-scaled slope change on the underlying curve.

## Step: Flat-curve edit under Normalize

**Context:** Normalize on. An expanded curve has a single keyframe (no real value range).
**Action:**
- Drag the single keyframe vertically

**Expected:**
- The keyframe moves off the zero line during the drag.
- The real value updates to a non-trivial number.
- After release, the curve gains a real range and renders normally within Normalize.

## Step: Close button collapses all

**Context:** One or more parameters are expanded.
**Action:**
- Click the Close icon in the curve pane's floating chrome

**Expected:**
- The curve pane disappears.
- All affected parameters' curve-expand toggles return to off.
- Component visibility state is cleared.
