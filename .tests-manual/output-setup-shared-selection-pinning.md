---
id: output-setup-shared-selection-pinning
title: Output Windows — Shared Selection & View Pinning
scope: output-window
tags: [projection-mapping]
added: 2026-08-31
added-in-version: 4.3
prerequisites:
  - A writable project is open with a setup containing at least one content send (SendToOutput with a texture), one slice, two surfaces, and one projector output.
  - Two output windows are open (Windows menu → add a second Output window).
---

Covers the one entity selection shared by all output windows and the per-window pin
that decouples what a window shows from that selection.

## Step: Selection is shared between output windows

**Action:**
Place both output windows side by side with their Flow Outliners shown. In window 1's
panel, click the item "Surface 1".

**Expected:**
- The item "Surface 1" is highlighted as selected in **both** windows' panels.
- Both windows switch their canvas to Surface 1's straight view.

## Step: Pin freezes one window's view

**Action:**
In window 1, select the projector output item so its output canvas is shown. Open
window 1's breadcrumb menu (the pin icon next to the breadcrumb) and choose
**Pin view to `<output name>`**. Then, in window 2's panel, click "Surface 1", then
"Surface 2".

**Expected:**
- Window 1 keeps showing the projector output's canvas during both clicks.
- A filled pin icon appears at the left of window 1's toolbar; hovering it shows
  "Pinned to `<output name>`".
- Window 2 follows the clicks (Surface 1's view, then Surface 2's view).
- The selection highlight in **both** panels follows the clicks (window 1's panel
  highlights Surface 2 even while its canvas stays pinned).

## Step: Unpinning resumes following

**Action:**
Click the filled pin icon in window 1's toolbar.

**Expected:**
- The pin icon disappears from the toolbar.
- Window 1 immediately switches to the shared selection's view (Surface 2).
- The breadcrumb menu now offers **Pin view to …** again (unchecked).

## Step: Deleting a pinned entity reverts the pin

**Action:**
Pin window 1 to "Surface 2" (select it in window 1, breadcrumb → Pin). In window 2,
right-click the "Surface 2" item and choose **Delete**.

**Expected:**
- Window 1 falls back to following the shared selection — no error, no frozen stale
  view; its pin icon disappears.

## Step: Graph focus clears the selection in all windows

**Action:**
Select "Surface 1" in either panel, then click any non-SendToOutput operator in the
graph window.

**Expected:**
- The selection clears in **both** output windows' panels (no highlighted item).
- Both outliners close (the existing auto-close when the focused op is not a SendToOutput), including a
  pinned window's panel — pinning affects only the shown canvas, not panel
  visibility.

## Step: The pin survives a restart

**Action:**
Pin window 1 to the projector output. Save the project and restart the editor.

**Expected:**
- After the project loads, window 1 shows the projector output's canvas, its
  toolbar shows the filled pin icon, and hovering it names the output.
