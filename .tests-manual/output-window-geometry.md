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

The Output Window renders CPU point lists, curves and geometry without draw ops. One segmented control at the top
left hides the sidebar or shows a table per domain or the stats in it; the render stays visible next to the sidebar.

## Step: CPU points

**Action:**
Select the [RadialCPoints] and look at the Output Window.

**Expected:**
- A segmented control floats at the top left: closed eye (sidebar hidden), points with their count, info.
- The points are drawn as with a [VisualizePoints] with default settings; dragging orbits the camera.
- The points icon opens a sidebar with the point table; hovering a row marks that point with a blue dot on the render.

## Step: Other lists

**Action:**
Select a [DefineLensFlare] (its output is a list of sprites, not points).

**Expected:**
- Only the table is shown, without the segmented control.

## Step: Curves

**Action:**
Select the [StringToCurves] with the text `TiXL`.

**Expected:**
- The control shows seven segments: closed eye, points, vertices, edges, primitives, parts (each with its count in
  small text) and info.
- With the sidebar hidden, the glyph outlines are drawn as thin lines with a dot on each curve point, and the summary
  next to the control names the curves and parts.
- The info segment shows points, vertices, closed curves, size, bounds and the attributes (e.g. `CodePoint per Part`).

## Step: Tables in the sidebar

**Action:**
Click the parts segment, then the primitives and the points segment.

**Expected:**
- The sidebar opens on the left with the default window background; the render moves into the area right of the
  splitter and below the toolbar, with nothing drawn behind the table or the toolbar. The glyphs are centered there
  and orbit around its middle. Dragging the splitter resizes both.
- **Parts** lists 4 rows with pivot, `Id`, `Seed` and `CodePoint`, `GlyphId`, `CharIndex` ... ; **Primitives** 5 rows
  (`Kind` curve, `Closed` yes); **Points** the positions with `HandleIn` and `HandleOut`.
- Hovering a row draws that element in blue on the render: a dot for a point or vertex, the outline of a primitive,
  the outlines of a part with a dot at its pivot. Parts hidden behind other geometry are drawn faintly.
- The list icon at the right of the control row maximizes the table; the render pauses until it is clicked again.

## Step: Highlight selected rows

**Action:**
In **Parts**, click the row of the `i`, Ctrl+click the row of the `L`, then hide the sidebar.

**Expected:**
- The summary shows `2 parts selected`.
- The outlines of the `i` (both curves) and the `L` stay drawn white, with a dot at each pivot. Orbiting the camera
  keeps the highlight on the glyphs.
- Shift+click extends the selection; clicking the only selected row clears it. Switching to another domain clears it.

## Step: Selection attribute

**Action:**
Connect a [SubdivideCurves] and a [SelectGeometry] (Every 4) after the [StringToCurves] and select the [SelectGeometry].

**Expected:**
- Every fourth point of each curve has a white dot; changing Offset moves the dots along the outline.

## Step: Geometry

**Action:**
Select the [CubeGeometry] and open the points, vertices, edges and primitives tables.

**Expected:**
- The parts segment is grayed out; its tooltip mentions parts.
- **Points** lists 8 rows, **Vertices** 24 with primitive and point, **Edges** 12 with both primitives filled in (the
  cube is closed), **Primitives** 6 with 4 vertices each; the counts in the control match.
- Attributes of each domain appear as extra columns, e.g. normals or texture coordinates.
- Selected primitives and edges are drawn thick; selected vertices show as dots pulled slightly into their face.

## Step: No gizmo behind tables

**Action:**
Select a [ScatterPointsInVolume] (it has a transform gizmo for its box) and maximize its table.

**Expected:**
- No gizmo is drawn while the sidebar is open; hiding the sidebar shows the gizmo on the box again.

## Step: Layout is kept per kind

**Action:**
Open the stats for the curves, select the [CubeGeometry], then the [StringToCurves] again.

**Expected:**
- Both show the stats in the sidebar at the same width; the point views keep their own layout.

## Step: Connection tooltip

**Action:**
Connect the [CubeGeometry] to a [GeometryToMesh] and hover the connection.

**Expected:**
- The tooltip shows the stats with the face count as headline, with no control and no render.

## Step: Overlay menu

**Action:**
With the [CubeGeometry] selected, click the eye icon next to the gizmo toggle in the output toolbar.

**Expected:**
- A popup lists Wireframe, Points, Point indices, Selection, Hover and Normals, each with its icon and a check mark
  when on.
- Turning Wireframe off hides the edges, Points the point dots; Point indices shows the vertex indices.
- A second output window keeps its own choices, and they survive a restart.
- For a texture or a scene output the icon is not shown.

## Step: Soft selection colors

**Action:**
Connect [CubeGeometry] -> [SubdivideGeometry] (Smoothness 0, Iterations 2) -> [SelectGeometry] (Domain Points, Mode
Custom, Code `return p.X * 3;`) and select the [SelectGeometry].

**Expected:**
- Points on the right side are white where the weight is about 1 and turn blue above 1; points on the left (negative
  weights) are red; points around the middle (weight 0) are not marked.
- Selections hidden behind the cube are drawn faintly.
