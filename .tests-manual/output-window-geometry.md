---
id: output-window-geometry
title: Points, Curves and Geometry in the Output Window
scope: output-window
tags: [geometry, points, output-window]
added: 2026-11-20
added-in-version: 4.4
prerequisites:
  - A project with a [RadialCPoints], a [StringToCurves] and a [CubeGeometry], none of them connected.
related-help:
  - ../.help/docs/getting-started/Introduction.md
  - ../.help/docs/using/ProceduralGeometry.md
---

The Output Window renders CPU point lists, curves and geometry without draw ops, and switches curves and geometry to
their stats.

## Step: CPU points

**Action:**
Select the [RadialCPoints] and look at the Output Window.

**Expected:**
- A segmented button offers **Points** and **Table**, with **Points** selected, next to the point count.
- The points are drawn as with a [VisualizePoints] with default settings.
- Dragging orbits the camera; the toolbar shows the camera controls as for a rendered scene.
- **Table** lists the point values as before.

## Step: Other lists

**Action:**
Select a [DefineLensFlare] (its output is a list of sprites, not points).

**Expected:**
- Only the table is shown, without the segmented button.

## Step: Curves

**Action:**
Select the [StringToCurves].

**Expected:**
- A segmented button offers **Render** and **Stats**, next to the contour and part count.
- **Render** draws the glyph outlines as thin lines.
- **Stats** lists anchors, segments, closed contours, size, bounds and the attributes (e.g. `CodePoint per Part`).

## Step: Geometry

**Action:**
Select the [CubeGeometry].

**Expected:**
- A segmented button offers **Render** and **Stats**, next to the face count.
- **Render** shows the shaded cube with its wireframe and vertex indices.
- **Stats** shows the same table as before: points, triangles, size, bounds, volume, "watertight", attributes.

## Step: Mode is kept per kind

**Action:**
Switch the curves to **Stats**, select the [CubeGeometry], then the [StringToCurves] again.

**Expected:**
- The cube is still rendered; the curves still show their stats.

## Step: Connection tooltip

**Action:**
Connect the [CubeGeometry] to a [GeometryToMesh] and hover the connection.

**Expected:**
- The tooltip shows the stats with the face count as headline, with no buttons and no render.
