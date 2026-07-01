---
id: dopesheet-curve-expand
title: DopeSheet Per-Parameter Curve Expand
added: 2026-04-19
added-in-version: 4.2
scope: timeline
tags: [user, essential]
prerequisites:
  - A project with at least one animated float parameter and one animated Vector3 parameter is open.
  - The Timeline window is visible in DopeView mode.
related-help: []
---

Right in the [ui:DopeSheet|dope sheet], you can open up any animated parameter and edit its actual curve
without leaving the [ui:Timeline|timeline]. This walks through expanding a parameter into its curve,
showing or hiding the X / Y / Z parts of a Vector3, and how hovering and selecting stay in
sync between the dope sheet and the curve view.

## Step: Expanding a float parameter

**Action:**
In the Timeline window (DopeView mode), find an animated float parameter row
and click its curve-expand icon (the small chevron in the row's header).

**Expected:**
- The timeline body splits vertically: dope sheet on top, a new [ui:CurveEditor|curve editor]
  pane below.
- The expanded parameter's curve is rendered in the new pane.

## Step: Collapsing the float parameter

**Action:**
With the parameter still expanded from the previous step, click the same
curve-expand icon again.

**Expected:**
- The curve editor pane disappears.
- The dope sheet reclaims the full height.

## Step: Expanding a Vector3 parameter

**Action:**
Find an animated Vector3 parameter and click its curve-expand icon.

**Expected:**
- Three component toggle buttons (`.x .y .z`) appear on that parameter's
  dope-sheet row, after the name.
- All three curves render in the curve pane.

## Step: Hovering a component toggle dims the others

**Action:**
With the Vector3 parameter expanded, hover the `.y` component toggle (don't
click yet).

**Expected:**
- The `.x` and `.z` curves fade to reduced opacity in both the dope layer and
  the curve pane.
- The `.y` curve stays at full opacity.

## Step: First click on a component isolates it

**Action:**
With all three components still visible (the default state), click the `.y`
component toggle.

**Expected:**
- Only the `.y` curve continues to render in both the dope layer and the curve
  pane.
- The `.x` and `.z` curves disappear; their toggle letters dim.

## Step: Adding a component back

**Action:**
With only `.y` visible from the previous step, click the `.z` component toggle.

**Expected:**
- Both `.y` and `.z` are visible.
- `.x` is still hidden.

## Step: Removing a component

**Action:**
With `.y` and `.z` visible from the previous step, click the `.y` component
toggle.

**Expected:**
- Only `.z` is visible.

## Step: Clicking the last visible component restores the default

**Action:**
With only `.z` visible from the previous step, click the `.z` component toggle.

**Expected:**
- All three components are visible again — clicking the last remaining one resets back to
  showing everything.

## Step: Hovering a dope-sheet row highlights its curves

**Action:**
Expand at least two parameters so multiple curves are visible in the curve
pane. Move the mouse over the background of one expanded parameter's
dope-sheet row (not over an icon, button, or keyframe).

**Expected:**
- The dope-sheet row gets the hovered background tint.
- In the curve pane, this parameter's curves stay at full opacity; other
  parameters' curves fade.

## Step: Hovering a curve line highlights its dope-sheet row

**Action:**
With at least two parameters expanded, move the mouse over a curve line
segment in the curve pane (not over a keyframe).

**Expected:**
- The dope-sheet row for that curve's parameter gets the hovered background
  tint.
- Other parameters' curves in the curve pane fade.

## Step: Hovering a keyframe highlights it in both views

**Action:**
With a parameter expanded and both views visible, hover a keyframe icon in the
curve pane.

**Expected:**
- The hovered keyframe's icon is outlined.
- The matching keyframe in the dope-sheet row (at the same point in time) is outlined the
  same way.

## Step: Reverse keyframe highlight from the dope sheet

**Action:**
Hover a keyframe in the dope-sheet row of an expanded parameter.

**Expected:**
- Every matching per-component keyframe at that point in time in the curve pane shows the
  same outline.

## Step: Component state resets on re-expand

**Action:**
With a Vector3 parameter expanded and `.x` hidden, click the parameter's
curve-expand icon to collapse it, then click it again to re-expand.

**Expected:**
- All three components are visible again — the previous hide is forgotten.

## Step: Parameter-name click replaces the keyframe selection

**Action:**
With at least one parameter having no keyframes currently selected, click that
parameter's name button (not the pin or curve icons).

**Expected:**
- The keyframe selection is replaced with that parameter's keyframes.

## Step: Shift+click adds to the selection

**Action:**
With some keyframes selected, shift+click a different parameter's name button.

**Expected:**
- That parameter's keyframes are added to the existing selection.

## Step: Ctrl+click removes from the selection

**Action:**
With a parameter's keyframes part of the current selection, Ctrl+click the
same parameter's name button.

**Expected:**
- That parameter's keyframes are removed from the selection.

## Step: Single-component selection reflects in the dope sheet

**Action:**
With a Vector3 parameter expanded, click one `.y` keyframe in the curve pane
to select it alone.

**Expected:**
- The matching dope-sheet row shows the keyframe at that point in time as selected.

## Step: Curve area grows when few parameters are open

**Action:**
With only one or two animated parameters and one of them expanded, just look
at how the two panes split.

**Expected:**
- The curve area takes the majority of the timeline height.
- The dope sheet is only as tall as it needs to be (params × layer height) —
  not stuck at 50/50.

## Step: Tall curve area centers instead of over-stretching

**Action:**
Continuing from the previous step — with the curve area clearly taller than
half the timeline — look at how the curves fit vertically.

**Expected:**
- The curves don't stretch to fill the whole tall area.
- They stay at a sensible size, centred with empty space above and below rather than
  blown up.

## Step: Drag latches to U-only or V-only

**Action:**
With a parameter expanded and a keyframe selected in the curve area, press
the keyframe and slowly drag mostly horizontally. Release.

Then press again and drag mostly vertically.

**Expected:**
- The first drag only moves the keyframe in time (sideways); its value doesn't change.
- The second drag only moves the keyframe in value (up/down); its timing doesn't change.
- The cursor changes to a horizontal or vertical arrow once it locks to one direction.
- On release the lock resets — the next drag starts free to go either way again.

## Step: Tangent edits are undoable

**Action:**
With a selected keyframe in the curve area, drag a tangent handle to a clearly
different angle, release, then press `Ctrl+Z`.

**Expected:**
- The tangent returns to its pre-drag angle and tension.
- Pressing `Ctrl+Shift+Z` redoes the edit.

## Step: Curve area auto-closes when the selection changes

**Action:**
With a parameter expanded, switch the graph selection to a different operator
whose animated parameters don't overlap with the current expanded set.

**Expected:**
- The curve area disappears — no valid expanded params remain.
- Re-selecting the original operator does NOT re-expand the parameter; you
  have to click the curve-toggle again.

## Step: Close button collapses everything

**Action:**
With one or more parameters expanded, click the Close icon in the curve
pane's floating chrome.

**Expected:**
- The curve pane disappears.
- All affected parameters' curve-expand toggles return to off.
- Component visibility state is cleared.
