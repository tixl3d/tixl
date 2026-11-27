---
id: straight-skeleton
title: Straight Skeleton
scope: geometry
tags: [regression]
added: 2026-11-27
added-in-version: 4.4
prerequisites:
  - An empty project is open.
related-help:
  - ../.help/docs/using/ProceduralGeometry.md
---

Verifies the [StraightSkeleton] geometry operator on convex and concave outlines and on an
outline with a hole: the skeleton curves, the raised roof faces, holes and the Height scale.

## Step: Skeleton of a convex outline

**Action:**
Add `[NGonCurves]` with `Sides` 10 and connect it to `[StraightSkeleton]`. Pin the
`[StraightSkeleton]` output and set `Output` to Skeleton.

**Expected:**
- The output shows a star of ten curves from the polygon's incenter to the corners.
- Points sit on the outline (Z 0) and at the event vertex (Z > 0). No curve runs along the
  outline itself.
- The `SkeletonTime` attribute exists on points; its values match the point Z with `Height` 1.

## Step: Skeleton of a concave outline

**Action:**
Add `[StringToCurves]` with the text `T` and connect it to `[StraightSkeleton]`.

**Expected:**
- The skeleton branches toward every concave corner of the T, and no curve leaves the glyph.
- Points at the glyph outline have Z 0; branch and split vertices have Z > 0.

## Step: Detailed glyphs

**Action:**
Add `[StringToCurves]` with the text `TiXL` and a font that has many outline points, connect it to
`[StraightSkeleton]`, set `Output` to Roof and pin the result.

**Expected:**
- All four glyphs, including the X, produce roof geometry.
- No contour fails; contours that need it are simplified slightly for a stable skeleton.
- The graph stays responsive with `Async` enabled.

## Step: Roof faces

**Action:**
Set `Output` to Roof.

**Expected:**
- One raised N-gon per outline edge forms a closed roof over the polygon, with the outline at
  Z 0 and ridges where facets meet.
- The roof has no overlaps or flipped facets; `[GeometryToMesh]` followed by `[DrawMesh]`
  lights it.

## Step: Height

**Action:**
Set `Output` to Both and drag `Height` from 0 to 2.

**Expected:**
- At 0 the skeleton and roof lie flat in the XY plane.
- The roof rises and falls smoothly with Height; the skeleton curves follow, while
  `SkeletonTime` stays unchanged.

## Step: Holes and multiple islands

**Action:**
Build `[NGonCurves]` (Sides 4, Radius 1) and a second `[NGonCurves]` (Sides 4, Radius 0.4)
into a `[CombineGeometry]`, then through `[MergeCurves]` with Mode Xor into `[StraightSkeleton]`.
Move the inner square outside the outer one and back inside.

**Expected:**
- Inside: the roof has facets on both the outer and inner edges and an opening at the hole;
  the skeleton includes the ring around the hole. One part.
- Outside: two separate roofs and two skeleton trees, one part each.
- No crash or warning in either case.

## Step: Async and status

**Action:**
Feed a `[StringToCurves]` with a long text into `[StraightSkeleton]` and toggle `Async`.

**Expected:**
- With Async on, the previous result stays visible and a progress indicator appears if the
  computation takes longer than half a second.
- With Async off, the result updates in the same frame.
- Switching both on and off repeatedly never leaves the output empty or stale.
