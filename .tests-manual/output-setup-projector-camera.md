---
id: output-setup-projector-camera
title: Output Setup — Projector Camera from an Aligned Surface
scope: output-setup
tags: [projection-mapping]
added: 2026-11-07
added-in-version: 4.4
prerequisites:
  - A projector bound to a Display or Projector output, a surface aligned on it (Align Points or dragged corners).
  - A graph with [UseProjectorCam] → [RenderTarget] → [SendToOutput] routed to the same output, drawing a small 3D object.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers the output card's **Projector Camera** section and the surface card's **Facing** preset.

## Step: Facing presets

**Action:**
Select the aligned surface. In its parameters, click **Floor**, then **Ceiling**, then **Wall** under **Facing**.

**Expected:**
- **Rotation (°)** shows a pitch of -90, then 90, then 0; yaw and position stay.
- Typing a pitch of 30 shows **Tilted** selected.
- Each click is one undo step.

## Step: Solving the camera

**Action:**
Select the output. In **Projector Camera**, set **Lens shift** to the projector's (0 0 if unknown, about 0 +50 for a projector on a table) and click **Solve from Aligned Surface**.

**Expected:**
- The hint reads "Solved from '<surface>': N° vertical, misses by M px", with M below a few pixels.
- **Use Manual Camera** becomes available.
- A 3D object placed on the surface through [UseProjectorCam] lands where it should on the wall.

## Step: A squarely seen surface uses the entered zoom

**Action:**
Align a surface the projector faces almost straight on, set **Zoom (°)** to the projector's vertical field of view, and solve.

**Expected:**
- The hint says the zoom was entered because the surface was seen too squarely to measure it.

## Step: Back to manual, and undo

**Action:**
Click **Use Manual Camera**, then press `Ctrl+Z`.

**Expected:**
- After the click the hint reads "Manual camera: not solved yet." and [UseProjectorCam] uses the manual look-at camera.
- `Ctrl+Z` brings the solved camera back.

## Step: Without an aligned surface

**Action:**
On an output with no surface mapped to it, click **Solve from Aligned Surface**.

**Expected:**
- The hint asks to map and align a surface first; nothing else changes.
