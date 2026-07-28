---
id: output-setup-panel-consistency
title: Setup Panel — Menu & Drag Consistency
scope: output-window
tags: [projection-mapping]
added: 2026-07-27
added-in-version: 4.3
prerequisites:
  - A writable project is open with a setup containing at least one content send
    (SendToOutput with a texture), one slice, one surface, one projector output,
    one reference image, and one prop.
---

Covers the uniform context-menu verbs across all setup entities, the parity between
sidebar-row menus and canvas frame-label menus, and direction-agnostic drag
connections.

## Step: Every entity kind offers the same core verbs

**Action:**
In the setup sidebar, right-click one row of each kind: a content send, a slice, a
surface, an output, a reference image, and a prop.

**Expected:**
- Every menu ends with the same core verbs where the kind supports them:
  **Duplicate**, **Rename**, **Delete**.
- Kind-specific extras come first: *Add slice* (content), *Add sub-region / Adjust
  aspect to slice / Clear content inputs* (surface), *Bind to display* (projector
  or display output).
- A prop offers Duplicate and Delete but no Rename (props have no name).
- A content send offers Rename (renames the op) but no Duplicate/Delete (it *is* a
  graph op — duplicate or delete it in the graph).

## Step: Duplicate works for every duplicable kind

**Action:**
Use the menu's Duplicate on a slice, an output, a reference image, and a prop.

**Expected:**
- Each creates a copy (named "… copy" where named), selects it, and saves the setup.
- A duplicated output starts with no display binding and no surface mappings — those
  stay with the original.

## Step: Canvas frame-label menu matches the sidebar menu

**Action:**
On the output canvas, right-click a surface's center label; compare against
right-clicking the same surface's sidebar row. Repeat for a slice label in the
content view.

**Expected:**
- Both menus show identical items in identical order.
- Choosing **Rename** from the canvas menu opens the inline rename field on the
  sidebar row.

## Step: Drag connections work in both directions

**Action:**
In the sidebar, drag a *surface row* onto an *output row*. Undo the mapping (or use
a second output), then drag the *output row* onto the *surface row*.

**Expected:**
- Both directions create the same surface→output mapping.

**Action:**
Repeat with the other connectable pairs, in both directions each: slice ↔ surface,
content send ↔ surface, slice ↔ output, content send ↔ output.

**Expected:**
- Every pair connects identically regardless of which row was picked up.
- The orange drop indicator only appears over rows that can accept the dragged kind.

## Step: Multi-selection menus stay consistent

**Action:**
Ctrl-click to select three entities of mixed kinds, then right-click one of them.

**Expected:**
- The per-entity actions (extras, Duplicate, Rename) show dimmed/disabled.
- The delete entry reads "Delete N" with the count of actually deletable entities
  and removes them all.

## Step: Every canvas drag undoes as one step

**Action:**
Perform each of these drags, pressing Ctrl-Z once after each: drag a slice's edge, corner,
and label in the content view; drag a sub-region's label and edge on the output canvas;
drag a measuring-line endpoint in Straight mode.

**Expected:**
- Each drag restores completely with a single Ctrl-Z (slice edits and measuring-line moves
  are newly undoable).
- The setup file is written once per completed drag, not while dragging (no disk churn
  during a slice drag).
- "Match target aspect" from the slice menu is also a single undoable step.

## Step: Structural edits undo as single steps

**Action:**
Perform each of these, pressing Ctrl-Z once after each: add a surface; duplicate an output;
drag a content send onto a surface (the connect); click a surface's in-gutter arrow with a
slice selected (the bind toggle); delete three multi-selected entities at once; draw a new
measuring line; click "Straighten" with traced lines; rename a slice.

**Expected:**
- Each action reverts completely with one Ctrl-Z — including the multi-delete (all three
  return) and the measuring line (created and dragged out, gone again as one step).
- Ctrl-Y re-applies each.
- Undoing an output deletion restores the output but not its display binding (bindings are
  per-machine, outside the setup file) — re-bind via the context menu.
- A drop that changes nothing (re-dropping a slice on the surface already showing it) adds
  no undo step.

## Step: Property-card fields undo as single steps

**Action:**
With a surface selected, in its card: toggle Render, drag the Anchor field, drag a slice's
Position (px) field for a while, and toggle an output's Send. Press Ctrl-Z once after each.

**Expected:**
- Each toggle and each complete field drag reverts with a single Ctrl-Z — a long drag is
  one step, not one per mouse-move.
- The setup file is written once per completed field gesture, not while dragging.
- Size (m) still undoes as one step including the re-projected corner pins.
