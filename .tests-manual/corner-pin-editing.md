---
id: corner-pin-editing
title: Corner-Pin Editing
scope: output-window
tags: [projection-mapping]
added: 2026-07-20
added-in-version: 4.3
prerequisites:
  - A writable project is open.
  - Familiarity with the Setup panel (see output-setup-presets).
related-help:
  - ../.tests-manual/output-setup-presets.md
---

Covers editing a surface's corner-pin mapping onto an output: opening an output's editing
canvas in Setup mode, dropping a surface's quad, dragging its four corners, undo/redo of a
drag, canvas pan/zoom, and persistence to the setup file.

## Step: Create a surface and an output

**Action:**
In the Output Window toolbar, click `View` and choose Output Mode "Setup" (this also opens the
panel). In the panel, click `+` next to SURFACES, then `+` next to OUTPUTS.

**Expected:**
- SURFACES lists a new `Surface 1`.
- OUTPUTS lists a new projector `P1`, shown as "unbound".

## Step: Open the output's editing canvas

**Action:**
Click `P1` in the OUTPUTS section.

**Expected:**
- The view area shows the output's frame — a dark rectangle with a thin border — fitted and
  centered in the view.
- A header reads `P1  ·  1920×1200`.
- Because `Surface 1` is not mapped to this output yet, a `+ Surface 1` button sits next to
  the header.

## Step: Map the surface onto the output

**Action:**
Click `+ Surface 1`.

**Expected:**
- A quad appears centered in the output frame, with a faint checker fill and the label
  `Surface 1` in its middle.
- Its four corners carry handles: the top-left is a small square, the other three are circles.
- The `+ Surface 1` button disappears (the surface is now mapped).

## Step: Drag a corner

**Action:**
Drag the top-left square handle a short distance and release.

**Expected:**
- The quad edges and the checker fill follow the handle live while dragging.
- The handle brightens while hovered/dragged; the corner stays where it is released.
- No other corner moves.

## Step: Undo and redo the drag

**Action:**
Press `Ctrl+Z`, then redo (`Ctrl+Y` or `Ctrl+Shift+Z`, per your keymap).

**Expected:**
- Undo returns the corner to its exact pre-drag position — the whole drag is a single undo
  step, not one per mouse-move.
- Redo restores the dragged position.

## Step: Pan and zoom the canvas

**Action:**
Right-drag in the view to pan; scroll the mouse wheel to zoom in and out.

**Expected:**
- The output frame and the quad pan and zoom together — the corner handles stay attached to
  the quad.
- The interaction feels the same as panning/zooming the Graph Window.

## Step: The mapping persists

**Action:**
Select `Surface 1` (or any other entity) in the panel, then click `P1` again. Optionally open
`.meta/Setup 1.setup.json` in the project folder.

**Expected:**
- The quad reappears exactly where it was left.
- The surface's `OutputMappings` entry — with the four `Quad` points in output pixels — is
  present in the setup JSON (written on drag release).

## Step: Corner selection on the canvas

**Action:**
With two mapped surfaces on one output, click a corner handle of the first surface. Then
shift-click a corner of the second surface, and ctrl-click one of the two to toggle it off
and on again.

**Expected:**
- A selected corner renders marked: status-colored fill with a bright rim, slightly larger.
- Shift adds to the corner selection across surfaces; ctrl toggles a single corner.
- Plain-clicking an *unselected* corner replaces the corner selection with just that corner.

## Step: Marquee-select corners

**Action:**
In the plain output view, left-drag on empty canvas space to draw a marquee across several
corner handles. Then left-click on empty space.

**Expected:**
- A selection rectangle appears while dragging; corners inside it are selected live.
- Shift-marquee adds to the existing corner selection; ctrl-marquee removes.
- Panning still works via right-drag/middle-drag — left-drag on empty space is the marquee.
- The empty click clears the corner selection (the entity selection keeps its own rules).

## Step: Group-drag selected corners

**Action:**
Select corners on both surfaces (marquee or shift-click), then drag one of the selected
corners.

**Expected:**
- All selected corners move together by the same offset, across both surfaces.
- Snapping applies to the dragged corner and the group follows it.
- A single Ctrl-Z undoes the whole group move at once (both surfaces restore); Ctrl-Y
  redoes it.

## Step: Corner selection follows the shown canvas

**Action:**
With corners selected, switch to another output (or another edit mode), then back.

**Expected:**
- The corner selection is cleared on the switch — it never carries over to a different
  canvas view.
