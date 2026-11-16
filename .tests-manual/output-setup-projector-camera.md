---
id: output-setup-projector-camera
title: Output Setup — Projector Camera from a Calibrated Surface
scope: output-setup
tags: [projection-mapping]
added: 2026-11-07
added-in-version: 4.4
prerequisites:
  - A projector bound to a Display or Projector output, a surface corner-pinned onto it (Calibrate or dragged corners).
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

## Step: The camera syncs to the surface

**Action:**
Select the output and tick **Is Projector**. In **Projector Camera**, set **Lens shift** to the projector's (0 0 if
unknown, about 0 +50 for a projector on a table). Leave **Sync to Surface Calibration** on.

**Expected:**
- **Sync to Surface Calibration** is ticked by default, and there is no solve or manual-camera button.
- The hint reads "Solved from '<surface>': N° vertical, misses by M px", with M below a few pixels once the surface is calibrated.
- A 3D object placed on the surface through [UseProjectorCam] lands where it should on the wall.
- Changing the lens shift re-solves right away.

## Step: A squarely seen surface uses the entered zoom

**Action:**
Corner-pin a surface the projector faces almost straight on, and set **Zoom (°)** to the projector's vertical field of view.

**Expected:**
- The hint says the zoom was entered because the surface was seen too squarely to measure it.

## Step: The camera follows calibration

**Action:**
Wire a mesh in front of the pinned surface through [UseProjectorCam] to the output, then select the surface and turn
on **Calibrate**. Drag one reference point slowly and let go, then press `Ctrl+Z`. Turn **Calibrate** off, then on
again and cancel with B on a game controller.

**Expected:**
- While dragging, the mesh moves with the pin on the projected picture; the zoom in the output's Projector section
  stays the same, the distance and the "misses by" value update.
- Letting go, or ending calibration with a game controller, measures the zoom again; the setup file is saved.
- `Ctrl+Z` puts the point back and the mesh with it.
- Cancelling with B puts the pin and the camera back to where they were when calibrating started.
- A surface filling the same output (right-click → **Fill**) is never what the camera is solved from.

## Step: Keeping the camera

**Action:**
Untick **Sync to Surface Calibration**, drag a corner of the pinned surface, then tick it again. Press `Ctrl+Z` twice.

**Expected:**
- While unticked, the mesh stays where it was and the hint ends with "Kept while sync is off."
- Ticking it again solves from the moved corner at once.
- Each tick is one undo step.

## Step: New 3D Content

**Action:**
1. On a projector output, select a surface or region corner-pinned onto it and traced on a reference photo, then
   open **Connect...** in the toolbar.
2. Choose **New 3D Content**. Press `Ctrl+Z`, then `Ctrl+Shift+Z`.
3. Open **Connect...** on a surface that only fills a projector output, or sits on a display output.
4. Select a surface that already shows content and open **Connect...**. Choose **New 2D Content**.

**Expected:**
- After 1: **New 3D Content** sits below **New 2D Content** even before any calibration; with the surface on several
  projectors it is a submenu of their names.
- After 2: the graph gains one snapped column, SurfaceGeometry → GeometryToMesh → DrawMeshUnlit → Group →
  UseProjectorCam → RenderTarget → a send named after the projector with "3D", with a SurfaceReferenceImage
  snapped to DrawMeshUnlit's Texture input; all of them are selected. Both surface inputs point at the surface and
  UseProjectorCam's Output Ref at the projector. The output's **Layers** show the new content at the top. The
  projector shows the surface's photo on the real surface, and the output's other layers still show around it.
  Undo removes the ops and the layer in one step; redo brings both back.
- After 3: **New 3D Content** is greyed out, and its tooltip says to corner-pin the surface onto a projector output first.
- After 4: **Connect...** is shown without emphasis on a surface that has content. The new content is created and the
  surface shows it; the previous content stays in the graph.

## Step: Without a pinned surface

**Action:**
Select a projector output with no surface corner-pinned onto it.

**Expected:**
- The hint asks to corner-pin a surface onto the output; [UseProjectorCam] warns that the camera isn't solved and
  renders from in front of the stage origin.

## Step: Measuring with the side view

**Action:**
On a projector output, drag the side view's distance value, then type the picture's height at the wide and the tele end and its lower edge's height above the lens, measured on a wall. Move the **Zoom** slider.

**Expected:**
- Changing the distance rescales the drawn picture and its edge values; **Zoom** and **Lens shift** stay.
- The two heights set the ends of the zoom range; the **Zoom** slider moves the solid picture between the two faint ones; the lower edge changes the vertical **Lens shift**; the drawing follows. Each drag or typed value is one undo step.
- With the switch on **Ratio**, a range of 1.48 1.62 on a 16:9 output reads about 21.5° 19.7° on **Zoom**, and the other way round.
- With both ends equal (a fixed lens) the second value is faded; the slider and both height fields stay editable.
- Typing 0.44 into the tele value of a 0.40 range is never interrupted, even when the value passes 0.4 on the way.
- Entering the ends the wrong way round (tele wider than wide) swaps them once the field is left.
- After a solve, the distance and zoom show the solved values.

## Step: Projector presets

**Action:**
Open the **Model** dropdown, choose **Save as Preset...**, enter a manufacturer and model, and save. Then change the lens shift and pick the preset again.

**Expected:**
- The dropdown shows the new model, checked; the preset is listed in every project.
- Picking it sets **Lens shift** and **Zoom** back to the preset's values. Right-click → **Remove** deletes it.

