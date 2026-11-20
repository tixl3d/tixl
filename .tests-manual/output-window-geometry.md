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

## Step: Curve table

**Action:**
With the [StringToCurves] selected and its text set to `TiXL`, click **Table**, then **Parts**.

**Expected:**
- A second segmented button offers **Anchors**, **Segments**, **Contours** and **Parts**.
- **Parts** lists 4 rows, one per glyph, with `Contour`, `Contours`, pivot, `Id`, `Seed` and the attribute columns
  `CodePoint`, `GlyphId`, `CharIndex`, `WordIndex`, `LineIndex`, `Advance`.
- **Contours** lists 5 rows (the `i` has two); `Closed` reads `yes`.
- **Anchors** lists positions; the `In` and `Out` columns are zero for corners and drawn faintly.
- The summary next to the buttons shows the row count, e.g. `4 parts`.

## Step: Highlight selected rows

**Action:**
In **Parts**, click the row of the `i`, Ctrl+click the row of the `L`, then click **Render**.

**Expected:**
- The summary shows `2 selected` in the table and `2 parts selected` after switching.
- The outlines of the `i` (both contours) and the `L` are drawn thicker in the selection color, each with a cross at
  its pivot. Orbiting the camera keeps the highlight on the glyphs.
- Back in **Table**, Shift+click extends the selection; clicking the only selected row clears it.
- Switching to **Anchors** clears the selection; selected anchors show a dot with their handles.

## Step: Geometry

**Action:**
Select the [CubeGeometry].

**Expected:**
- A segmented button offers **Render** and **Stats**, next to the face count.
- **Render** shows the shaded cube with its wireframe and vertex indices.
- **Stats** shows the same table as before: points, triangles, size, bounds, volume, "watertight", attributes.

## Step: Geometry table

**Action:**
With the [CubeGeometry] selected, click **Table** and step through **Points**, **Faces**, **Corners** and **Edges**.

**Expected:**
- **Parts** is grayed out; its tooltip says the geometry has no parts.
- **Points** lists 8 rows with positions, **Faces** 6 rows with 4 corners each and their centers, **Corners** 24 rows
  with face and point, **Edges** 12 rows with both faces filled in (the cube is closed).
- Attributes of each domain appear as extra columns, e.g. normals or texture coordinates.
- Select a face and a few edges (Ctrl+click), switch to **Render**: the face outline and the edges are drawn thick.
  Selected corners show as dots pulled slightly into their face.

## Step: No gizmo behind tables

**Action:**
Select a [ScatterPointsInVolume] (it has a transform gizmo for its box) and switch its output to **Table**.

**Expected:**
- No gizmo is drawn behind the table. In **Points** the gizmo appears again, placed on the box.

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
