---
id: graph-connection-rerouting
title: Rerouting Connection Lines with Bend Points
added: 2026-05-31
added-in-version: 4.3
scope: graph-window
tags: [user, essential]
prerequisites:
  - A project you can edit is open in the Graph Window.
  - A graph with at least two operators connected by a cable you can see clearly.
related-help:
  - ../.help/docs/using/ReroutingConnections.md
---

Covers pulling a cable aside with bend points so it stops running through the
nodes between its endpoints. Bend points are cosmetic: they change the drawn
route only, never what the graph evaluates.

## Step: Add a bend point to a cable

**Action:**
Hold `Alt` and hover the middle of a cable. A marker follows the cursor. Click
once where you want the cable to run instead.

**Expected:**
- A bend point appears at that position and the cable now passes through it.
- The cable keeps its type colour and its rounded corners.
- The graph's output is unchanged — the output window keeps rendering the same
  result.

## Step: Place a bend point in one drag

**Action:**
Hold `Alt`, press the mouse button on the cable, and — without releasing — drag to
where the cable should go. Release.

**Expected:**
- The cable follows the cursor while dragging.
- On release the bend point stays where you dropped it.

## Step: Move and remove a bend point

**Action:**
Without holding any modifier, drag the round handle at the bend. Then right-click
the same handle.

**Expected:**
- The handle under the cursor fills in while you point at it.
- Dragging moves the handle and the cable follows it, staying made of straight
  segments between the handles.
- The right-click removes the handle and the cable returns to its automatic route.
- **No context menu opens** over the graph when the right-click removed a handle.

## Step: The context menu still works away from handles

**Action:**
Wait a moment, then right-click on empty canvas, and separately on an operator.

**Expected:**
- The canvas context menu opens as it did before this feature existed.
- Right-clicking an operator still shows that operator's menu.
- Only a right-click that lands on a bend point handle is diverted to removing it.

## Step: Dragging a handle does not need Alt

**Action:**
Add a bend point with `Alt`+click, release both the mouse and `Alt`, then move the
handle again with a plain drag.

**Expected:**
- The handle moves even though `Alt` is not held.
- Clicking the cable away from any handle still opens the operator search, as it
  did before the cable was rerouted.

## Step: Dragging a handle does not start a selection fence

**Action:**
Press and hold on a bend point handle, drag it a short distance, then release.

**Expected:**
- No selection rectangle appears at any point during the drag.
- Nothing else in the graph is selected or deselected by the gesture.

## Step: A dimmed rerouted cable stays readable

**Action:**
Let the graph go idle so its cables fade out, then look at the cable that has a
bend point.

**Expected:**
- The rerouted cable fades in colour but keeps a visible thickness — it does not
  thin down to a hairline.
- Its bend point handle stays visible and can still be grabbed.

## Step: Add several bend points around a stacked node

**Action:**
Stack two operators vertically so a cable from a third operator would run through
them. Hold `Alt` and click two or three points along a route that goes around the
stacked nodes instead.

**Expected:**
- The cable follows each point in the order you added them, as straight segments.
- Every bend point is shown as a draggable handle.
- None of the bend points replaces another; adding one between two existing points
  inserts it in the correct order rather than appending it at the end.

## Step: Snapped connections are left alone

**Action:**
Snap two operators together so their cable collapses to a short segment between
them. Hold `Alt` and hover that segment, then click it.

**Expected:**
- No bend point is added and no bend-point marker follows the cursor.
- The snapped cable stays visible — it does not disappear.
- The cable's normal behaviour still applies (clicking it can insert an operator).

## Step: Cancel a bend-point drag

**Action:**
Hold `Alt` and start dragging a bend point, then press `Esc` before releasing the
mouse button.

**Expected:**
- The cable snaps back to its previous route.
- The abandoned bend point is not added.

## Step: Undo a bend point

**Action:**
Add a bend point, then press `Ctrl+Z`.

**Expected:**
- The bend point disappears and the cable returns to its automatic route.
- Pressing `Ctrl+Shift+Z` (redo) brings the bend point back.

## Step: Bend points survive a reload

**Action:**
Add a bend point to a cable, save the project, and restart the editor. Reopen the
project and look at that cable.

**Expected:**
- The cable still follows the bend point after the restart.
- No warning about connection waypoints appears in the console.

## Step: Alt on the background still behaves

**Action:**
With no operator selected, hold `Alt` and press the mouse button on empty canvas,
then release without moving. Then repeat without holding `Alt`.

**Expected:**
- With `Alt` held, nothing is created and no operator search opens.
- Without `Alt`, the canvas behaves as before (a long press still opens the
  operator search, if that option is enabled in the settings).
