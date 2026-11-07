---
id: output-setup-surface-content
title: Output Setup — Content Designed on a Surface
scope: output-setup
tags: [projection-mapping]
added: 2026-11-07
added-in-version: 4.4
prerequisites:
  - A writable project is open, with an output setup that has one Display or Projector output besides the Default one.
  - A surface traced on a reference photo, with a region inside it. Neither shows content, and neither is connected to an output.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers **Connect...**, [SurfaceReferenceImage] and [SendToOutput]'s **Resolution From: Surface Aspect**.

## Step: Connect... appears only for a surface without content

**Action:**
Select the region in the Flow Outliner. Then select a surface that already shows content.

**Expected:**
- With the region selected, the output view's toolbar shows **Connect...**.
- With the surface that shows content selected, **Connect...** is not shown.

## Step: New content for a region

**Action:**
Select the region, choose **Connect...** → **New 2D Content**.

**Expected:**
- The open composition gets a row of five ops: [SurfaceReferenceImage] → [Layer2d] → [Group] → [RenderTarget] → [SendToOutput], all connected. The [Group] is selected.
- The [SendToOutput] is named after the region and its **Resolution From** is **Surface Aspect**.
- The Flow Outliner shows the new content with a slice connected to the region.
- The region's parent surface (not the region) is connected to the output; the region shows no "own pin" status.
- The console says to add content to the Group and to align the points.

## Step: The photo fills the region without distortion

**Action:**
Look at the [SurfaceReferenceImage] output and at the region's content on the Board.

**Expected:**
- The output is the straightened photo of just the region's part of the wall, upright.
- On the Board, the photo inside the region lines up with the photo of the wall around it.
- The [SendToOutput] renders at the region's size in metres times its pixels per metre (check its preview size).

## Step: The photo follows the setup

**Action:**
In Straight, move or resize the region.

**Expected:**
- [SurfaceReferenceImage] updates to the region's new part of the photo.
- The render size of the [SendToOutput] follows the new proportions.

## Step: One undo step

**Action:**
Press `Ctrl+Z` once.

**Expected:**
- The five ops, the content source, the slice and the output connection are all gone.
- `Ctrl+Shift+Z` (redo) brings all of them back.

## Step: Resolution From warns without a surface

**Action:**
Disconnect the region's content in the Flow Outliner.

**Expected:**
- The [SendToOutput] shows a warning that no surface shows it, and renders at its Resolution or the output's size.
