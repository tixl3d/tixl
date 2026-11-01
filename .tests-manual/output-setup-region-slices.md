---
id: output-setup-region-slices
title: Output Setup — Slices from Regions
scope: output-window
tags: [projection-mapping]
added: 2026-11-01
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup has one surface "Wall", 3×2 m, mapped to an output.
  - The Graph window, the Parameter window and one output window in setup mode with its Flow Outliner shown are visible.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers **Create Slices from Regions**: one picture the size of a surface, cut into a slice per region from where
each region sits on the surface.

## Step: One slice per region, in place

**Action:**
1. Right-click "Wall" → **Add region** twice, and rename the regions "TV 1" and "TV 2".
2. In the Straight view, place "TV 1" over the left half of "Wall" and "TV 2" over the right half, both full height.
3. Right-click "Wall" in the outliner and choose **Create Slices from Regions**.

**Expected:**
- A SendToOutput op named "Wall" is added to the graph and appears in CONTENT, selected, with slices "TV 1" and
  "TV 2" under it. Its Resolution has a 3 : 2 aspect.
- On its Board card, the slice "TV 1" covers the left half and "TV 2" the right half.
- SURFACES shows a connection from each slice to its region. A picture crossing the middle of the op's output
  continues without a jump from "TV 1" to "TV 2" on the output.

## Step: Undo takes back op and slices together

**Action:**
Press Ctrl+Z once.

**Expected:**
- The op, its CONTENT item, both slices and their connections are gone. Ctrl+Y restores them.

## Step: Updating keeps the routes

**Action:**
1. Route the slice "TV 1" to the output as well, by dragging it onto a patch.
2. Shrink "TV 2" to half its width, and add a third region "TV 3" in the free area.
3. Right-click "Wall" and choose **Update Slices from Regions**.

**Expected:**
- No second op is added. "TV 2" now covers only its new area on the card, and "TV 3" gets a new slice.
- The route from "TV 1" to the patch is still there, so that slice now has two consumers and "TV 1" gets a new
  slice of its own while the patch keeps the shared one.

## Step: Re-cutting selected content

**Action:**
Add another SendToOutput op. Select it in CONTENT, Ctrl+click "Wall", right-click "Wall" and choose
**Create Slices from Regions on \<name\>**.

**Expected:**
- All three regions now show new slices of the selected content, and its Resolution is set to the surface's aspect.

## Step: Offered only where it applies

**Action:**
Right-click a surface without regions, then right-click the region "TV 1".

**Expected:**
- Neither menu offers the entry.
