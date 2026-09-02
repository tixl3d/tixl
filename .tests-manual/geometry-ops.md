---
id: geometry-ops
title: Procedural Geometry Ops
scope: geometry
tags: [regression]
added: 2026-09-02
added-in-version: 4.3
prerequisites:
  - An empty project is open.
related-help:
  - ../.help/docs/using/ProceduralGeometry.md
---

Verifies the CPU procedural geometry chain: generating, beveling, transforming and
compiling geometry for rendering.

## Step: Building the basic chain

**Action:**
Add `[CubeGeometry]`, `[GeometryToMeshBuffers]` and `[DrawMesh]` and connect them
in that order. Pin the `[DrawMesh]` output.

**Expected:**
- A lit, solid cube renders (not inside-out, no missing faces).
- The wire between the geometry ops is teal-green; the compiled mesh wire is red.

## Step: Beveling

**Action:**
Insert a `[BevelGeometry]` between `[CubeGeometry]` and `[GeometryToMeshBuffers]`.

**Expected:**
- All twelve edges and eight corners show smooth rounded bevels.
- No dark notches, spikes or holes at the corners.

## Step: Animating the bevel width

**Action:**
Drag the `Width` parameter continuously between 0 and 0.3.

**Expected:**
- The bevel follows interactively at full frame rate.
- The width stops growing when the automatic clamp kicks in; no self-intersections appear.

## Step: Chamfer and flat shading

**Action:**
Set `Segments` to 1 and `Roundness` to 0, then enable `FlatShading`.

**Expected:**
- The bevel becomes a straight chamfer.
- With `FlatShading` enabled, every face reads as a hard plane (no smooth gradients
  across the bevel strips).

## Step: Round corners

**Action:**
Set `Segments` to 4 and `Roundness` to 1, then enable `RoundCorners`.

**Expected:**
- Corners change from a flat fan into a spherical patch: the edge profile
  continues through the corner in concentric rings.
- No spikes at the corners, even when `Width` is dragged up to the clamp.

## Step: Transforming before the bevel

**Action:**
Insert a `[TransformGeometry]` between `[CubeGeometry]` and `[BevelGeometry]` and
set its `Scale` to (2, 0.5, 1).

**Expected:**
- The stretched box shows even bevels on all edges (the bevel adapts to the
  transformed shape rather than being stretched with it).

## Step: Triangulation passthrough

**Action:**
Insert a `[TriangulateGeometry]` directly after `[CubeGeometry]`.

**Expected:**
- The rendered result is visually unchanged — triangulating before beveling adds
  no visible seams (flat edges between coplanar triangles produce no bevels).
