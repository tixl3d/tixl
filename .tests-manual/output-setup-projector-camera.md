---
id: output-setup-projector-camera
title: Output Setup — Projector Camera from a Calibrated Surface
scope: output-setup
tags: [projection-mapping]
added: 2026-11-07
added-in-version: 4.4
prerequisites:
  - A projector bound to a Display or Projector output, a surface aligned on it (Calibrate or dragged corners).
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
Select the output. In **Projector Camera**, set **Lens shift** to the projector's (0 0 if unknown, about 0 +50 for a projector on a table) and click **Solve from Calibrated Surface**.

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

## Step: The camera follows calibration

**Action:**
Solve the camera from a surface, wire a mesh in front of that surface through [UseProjectorCam] to the output, then select the surface and turn on **Calibrate**. Drag one reference point slowly, then press `Ctrl+Z`. Turn **Calibrate** off, then on again and cancel with B on a game controller.

**Expected:**
- While dragging, the mesh moves with the pin on the projected picture; the zoom in the output's Projector section stays the same, the distance and the "misses by" value update.
- `Ctrl+Z` puts the point back and the mesh with it.
- Cancelling with B puts the pin and the camera back to where they were when calibrating started.
- A surface filling the same output (right-click → **Fill**) doesn't change what **Solve from Calibrated Surface** solves from.

## Step: New 3D Content

**Action:**
1. With the camera solved, select a surface or region on that output that shows nothing yet and that is traced on a
   reference photo, then open **Connect...** in the toolbar.
2. Choose **New 3D Content**.
3. Select another traced surface without content on the same projector and choose **New 3D Content** again.
4. Choose it once more on the first surface. Press `Ctrl+Z` twice.
5. Open **Connect...** on a surface whose output has no solved camera.

**Expected:**
- After 1: **New 3D Content** sits below **New 2D Content**; with the surface on several solved projectors it is a
  submenu of their names.
- After 2: the graph gains SurfaceGeometry → GeometryToMesh → DrawMeshUnlit (textured by SurfaceReferenceImage) →
  Group → UseProjectorCam → RenderTarget → SendToOutput, with the Group selected. The output's **Layers** show a new
  content item named after the projector with "3D", at the top. The projector shows the surface's photo on the
  real surface, and the output's other layers still show around it.
- After 3: only a SurfaceGeometry, GeometryToMesh, DrawMeshUnlit and SurfaceReferenceImage are added, below the first
  branch, into the same Group; no second UseProjectorCam or send. The second surface's photo lands on it too.
- After 4: nothing is added; the first surface's SurfaceGeometry is selected. Each `Ctrl+Z` removes one whole
  addition, graph and setup together.
- After 5: **New 3D Content** is greyed out, and its tooltip says to solve the projector camera first.

## Step: Without an aligned surface

**Action:**
On an output with no surface mapped to it, click **Solve from Calibrated Surface**.

**Expected:**
- The hint asks to map and align a surface first; nothing else changes.

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
- After **Solve from Calibrated Surface**, the distance and zoom show the solved values.

## Step: Projector presets

**Action:**
Open the **Model** dropdown, choose **Save as Preset...**, enter a manufacturer and model, and save. Then change the lens shift and pick the preset again.

**Expected:**
- The dropdown shows the new model, checked; the preset is listed in every project.
- Picking it sets **Lens shift** and **Zoom** back to the preset's values. Right-click → **Remove** deletes it.

