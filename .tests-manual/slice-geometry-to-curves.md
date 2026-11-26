---
id: slice-geometry-to-curves
title: SliceGeometryToCurves Operator
scope: operators
tags: [user]
added: 2026-11-26
added-in-version: 4.4
prerequisites:
  - A project with an output window showing a graph is open.
---

Covers cutting geometry into contour curves with SliceGeometryToCurves and feeding the result into the other
curve operators.

## Step: Slice a cube

**Action:**
Create a CubeGeometry, connect it to SliceGeometryToCurves and view the result with GeometryToMesh or a curve
drawing operator. Keep the default parameters.

**Expected:**

- One closed square outline appears at z = 0, around the middle of the cube.
- The status reads "1 curves, 1 closed."

## Step: Several slices and a tilted plane

**Action:**
Set Slice Count to 5 and Spacing to 0.2. Then set Normal to (1, 1, 1).

**Expected:**

- Five parallel outlines appear, centered on Center and 0.2 apart; slices outside the cube are empty.
- With the tilted normal, the middle slice is a hexagon; all slices stay parallel.
- Connecting FilterGeoPartsByIndex picks single slices by index.
- The geometry's attributes list an int SliceIndex on parts, counting 0 to 4 along the normal.

## Step: Fill and extrude the slices

**Action:**
Set Normal back to (0, 0, 1) and Slice Count to 1. Connect the result to FillCurves, then to ExtrudeCurves.

**Expected:**

- FillCurves produces a filled square; ExtrudeCurves produces a solid slab.
- For a hollow input (e.g. a cube combined with a smaller cube whose faces point inward), the inner outline
  becomes a hole.

## Step: Imported meshes and edge cases

**Action:**
Slice a mesh loaded with LoadObjGeometry without MergeVertices. Move Center so the plane passes exactly through a row
of vertices. Set Normal to (0, 0, 0). Move Center far outside the geometry.

**Expected:**

- The outlines of the imported mesh are continuous curves, not broken at UV or normal seams.
- A plane through existing vertices still produces clean, unbroken outlines.
- A zero normal gives the warning "The plane normal is zero." and no curves.
- A plane outside the geometry gives "No plane intersects the polygons." and no curves.
