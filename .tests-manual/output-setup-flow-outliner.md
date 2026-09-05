---
id: output-setup-flow-outliner
title: Output Setup — Flow Outliner Strip
scope: output-window
tags: [projection-mapping]
added: 2026-09-05
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup has at least one SendToOutput with content,
    one surface mapped to an output, and one patch (the "Output Setup — Patches" set builds
    this).
  - The graph window and one output window are visible; the output window is at least
    1200 px wide.
---

Covers Phase B.2/B.3 of the UI restructuring: the left setup panel is replaced by the
**Flow Outliner**, a strip under the output canvas with columns along the content flow and
a shelf for the kinds outside it. Edges between columns are not part of this slice.

## Step: The strip opens with the send op

**Action:**
Click the SendToOutput op in the graph window.

**Expected:**
- A strip appears at the **bottom** of the output window, under the canvas, with a header
  row (setup name with a chevron, a muted breadcrumb, a collapse chevron at the right) and
  five columns headed **CONTENT · SURFACES · OUTPUTS · LOCAL BINDINGS** and a narrower shelf
  headed **REFERENCE IMAGES** with **PROPS** below it.
- CONTENT lists the send with its slice indented; SURFACES the surface; OUTPUTS the output
  with its patch indented; LOCAL BINDINGS one dimmed row per display of this machine,
  labelled "Local / Display N" with its resolution.
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
  minimum (header plus a few rows) and a maximum (about two thirds of a 900 px window).
- After 2: the strip collapses to its header row and the canvas takes the space; the
  second click restores the previous height.
- After 3: the strip disappears entirely and the list icon appears in the toolbar; clicking
  it brings the strip back at its previous height.

## Step: Rows behave as the panel rows did

**Action:**
1. Hover the surface row, then the send row.
2. Click the patch row, then right-click it.
3. Drag the send row onto the output row.

**Expected:**
- After 1: hovering the surface lights the input arrow of its output and the trailing
  gutter of its slice and send; hovering the send lights the input arrows of the surface and
  the patch — the same cross-highlights as before, now across columns.
- After 2: the patch is selected, the Parameter window shows its card, and the breadcrumb
  reads the slice feeding it, the patch, and nothing after; the context menu offers Use on
  Surface, Duplicate, Rename, Delete.
- After 3: a second patch appears under the output.

## Step: Bindings column reflects the machine

**Action:**
Right-click the output row → **Bind to display** → the first display. Then **Stop
presenting** from the same menu.

**Expected:**
- While bound, the "Local / Display 1" row is no longer dimmed and shows the output's name
  as its status instead of the resolution; the output row shows "Local / Display 1".
- After unbinding both revert. Clicking a bindings row selects nothing and opens no menu.
