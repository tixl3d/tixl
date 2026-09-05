---
id: output-setup-flow-outliner
title: Output Setup — Flow Outliner Strip
scope: output-window
tags: [projection-mapping]
added: 2026-09-05
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup has at least one SendToOutput with content, one surface mapped to an output, and one patch (the "Output Setup — Patches" set builds this).
  - The graph window and one output window are visible; the output window is at least 1200 px wide.
---

Covers Phase B.2–B.4 of the UI restructuring: the left setup panel is replaced by the
**Flow Outliner**, a strip under the output canvas with columns along the content flow, a
shelf for the kinds outside it, and connections drawn between the items along the routing.

## Step: The strip opens with the send op

**Action:**
Click the SendToOutput op in the graph window.

**Expected:**
- A strip appears at the **bottom** of the output window, under the canvas, with a header
  item (setup name with a chevron, a muted breadcrumb, a collapse chevron at the right) and
  five columns headed **CONTENT · SURFACES · OUTPUTS · LOCAL BINDINGS** and a narrower shelf
  headed **REFERENCE IMAGES** with **PROPS** below it.
- The CONTENT column lists the "SendToOutput" item with "Slice 1" indented under it; SURFACES
  lists "Surface 1"; OUTPUTS lists "P1" with "Patch 1" indented under it; LOCAL BINDINGS one
  dimmed item per display of this machine, labelled "Local / Display N" with its resolution.
- Nothing docks to the left of the canvas anymore.

## Step: Splitter and collapse

**Action:**
1. Drag the strip's top edge upward by about 100 px, then downward past the middle of the
   window.
2. Click the chevron at the right end of the header, then click it again.
3. Open the toolbar's breadcrumb menu and untick **Show Flow Outliner**; then click the
   list icon at the left of the toolbar.

**Expected:**
- After 1: the strip grows and the canvas shrinks accordingly; the height stops at a
  minimum (header plus a few items) and a maximum (about two thirds of a 900 px window).
- After 2: the strip collapses to its header row and the canvas takes the space; the
  second click restores the previous height.
- After 3: the strip disappears entirely and the list icon appears in the toolbar; clicking
  it brings the strip back at its previous height.

## Step: Items behave as the panel items did

**Action:**
1. Hover the "Surface 1" item in the SURFACES column, then the "SendToOutput" item in the
   CONTENT column.
2. Click the "Patch 1" item in the OUTPUTS column, then right-click it.
3. Drag the "SendToOutput" item (CONTENT) onto the "P1" item (OUTPUTS).

**Expected:**
- After 1: with "Surface 1" hovered, its connections to "Slice 1" and to "P1" turn fully
  opaque and thicker. With "SendToOutput" hovered nothing changes on the connections (they
  attach to its slice, not to it). Items carry no trailing icons or routing text anymore; only
  a plug's resolution and an output's "unbound" remain as status.
- After 2: "Patch 1" is selected, the Parameter window shows the Patch card, and the header
  breadcrumb reads "Slice 1 → Patch 1"; the context menu offers Use on Surface, Duplicate,
  Rename, Delete.
- After 3: a "Patch 2" item appears under "P1".

## Step: Connections follow the routing

**Action:**
1. Look at the strip with nothing hovered.
2. Hover the "Surface 1" item, then click the "Slice 1" item.
3. Collapse the "P1" item with its chevron, then expand it again.
4. Right-click "P1" → **Split into 2×2**, then Ctrl+Z.

**Expected:**
- After 1: faint curves run from the right edge of "Slice 1" to the left edge of "Surface 1"
  and of "Patch 1" (in the texture colour, magenta), from "Surface 1" to "P1" (green), and
  from "P1" to "Local / Display 1" (gray) if it is bound; if it is not, "P1" carries the
  muted status "unbound" at its right end and no curve. Lines pass under the items, never
  over them.
- After 2: the curves touching "Surface 1" turn fully opaque and thicker while hovered; with
  "Slice 1" selected, both of its curves stay emphasized.
- After 3: while "P1" is collapsed, the curve from "Slice 1" ends at "P1" itself; expanded,
  it ends at "Patch 1" again.
- After 4: four curves fan out from "Slice 1" to "Patch 1" … "Patch 4", one per tile; the
  undo returns to one.

## Step: Kind colours and the bind arrows

**Action:**
1. Look at the column headers, then click the "Slice 1" item, then the "Surface 1" item, then
   the "SendToOutput" item.
2. With "Slice 1" selected, click the arrow at the left of the "Surface 1" item, then click it
   again.

**Expected:**
- After 1: the CONTENT header is tinted magenta, SURFACES green, OUTPUTS gray. A selected
  content or slice item fills magenta, a selected surface fills green, a selected output
  fills gray; hovering tints the item in the same colour. With "Slice 1" selected, an arrow
  appears at the left of every surface, output and patch item, filled on the ones showing
  that slice ("Surface 1", "Patch 1"). With "Surface 1" selected the arrows sit on the
  output items only. With "SendToOutput" selected there are **no** arrows at all.
- After 2: the first click unbinds "Surface 1" from "Slice 1" (its curve disappears, the
  arrow hollows); the second binds it again.

## Step: Del deletes the selection

**Action:**
1. Click the "Patch 2" item, then press Del with the mouse still over the strip.
2. Click "Patch 1", then double-click it to start renaming, press Del while the name field is
   active, then Escape.
3. Press Ctrl+Z.

**Expected:**
- After 1: "Patch 2" is gone from under "P1".
- After 2: Del only edits the text; "Patch 1" survives.
- After 3: "Patch 2" is back.

## Step: Bindings column reflects the machine

**Action:**
Right-click the output item → **Bind to display** → the first display. Then **Stop
presenting** from the same menu.

**Expected:**
- While bound, the "Local / Display 1" item is no longer dimmed and shows the output's name
  as its status instead of the resolution; the output item's "unbound" status is gone and a
  gray curve joins the two.
- After unbinding both revert. Clicking a bindings item selects nothing and opens no menu.
