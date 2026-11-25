---
id: selected-keyframes-toolbar
title: Editing Selected Keyframes in the Timeline Toolbar
added: 2026-11-25
added-in-version: 4.4
scope: timeline
tags: [user]
prerequisites:
  - A project with an operator whose float parameter has three keyframes at different values (e.g. 0, 1 and 2) is open.
  - The timeline is visible and the window is wide enough that the toolbar has room to the right of the transport buttons.
related-help: []
---

When keyframes are selected, the timeline toolbar summarizes them in one line: how many, which parameter,
their value range and their interpolation. The range ends scale the values, the range bar between them offsets them.

## Step: Showing the summary

**Action:**
Select the operator, then select all three keyframes of the animated parameter in the dope sheet.

**Expected:**
- Between the transport buttons and the view-mode toggle, the toolbar shows an interpolation icon, `3×` and the parameter name, then `0.00`, a short range bar with end ticks, and `2.00`.
- A thin vertical line separates the line from the view-mode toggle.
- Deselecting all keyframes hides the line again.

## Step: Typing a value with a single click

**Action:**
With the three keyframes selected, click the upper value `2.00` once, type `4` and press `Enter`.

**Expected:**
- The field turns into a text input after the first click.
- The keyframes now read 0, 2 and 4: the values were scaled around the lower end.

## Step: Scaling by dragging

**Action:**
Hover the lower value, then drag it to the left.

**Expected:**
- Hovering highlights the field and shows a left-right resize cursor.
- While dragging, the lowest keyframe follows the value and the middle keyframe moves proportionally; the highest keyframe stays in place.

## Step: Offsetting all values

**Action:**
Hover the range bar between the two values, then drag it to the right and release.

**Expected:**
- While hovering, the range bar and both values light up together.
- All three keyframes move up by the same amount; the range in the toolbar shifts while its width stays the same.

## Step: Undoing an edit

**Action:**
Press `Ctrl+Z` once.

**Expected:**
- The offset from the previous step is reverted in a single undo step, and the toolbar shows the previous range.

## Step: Changing the interpolation

**Action:**
Click the interpolation icon at the start of the line and choose **Constant**.

**Expected:**
- A menu titled *Interpolation* lists Linear, Smooth (Clamped), Smooth, Horizontal and Constant, with icons, and checkmarks on the types currently used.
- After choosing **Constant**, the curve steps between the keyframes and the toolbar icon shows the constant interpolation.

## Step: Mixed interpolation

**Action:**
Change one keyframe to **Linear** with its context menu, then select all three again.

**Expected:**
- The toolbar shows `?` instead of an interpolation icon, and the menu has checkmarks on both Linear and Constant.

## Step: Single keyframe

**Action:**
Select only one keyframe.

**Expected:**
- The toolbar shows the parameter name and a single value without a range bar. Dragging or typing it changes that keyframe's value.

## Step: Narrow toolbar

**Action:**
Make the window narrower until the toolbar runs out of room.

**Expected:**
- The parameter name disappears first, then the whole line. Nothing overlaps the transport buttons or the view-mode toggle.
