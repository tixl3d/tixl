---
id: point-separator-conventions
title: Point Separator Conventions
scope: point-rendering
tags: [regression]
added: 2026-09-02
added-in-version: 4.2
prerequisites:
  - An empty project is open.
---

Verifies that NaN separators in point lists (written by `Point.Separator()` and
point generators) break line-style rendering consistently across draw operators.
There is exactly one convention: a point with `Scale.x` = NaN is a separator —
never drawn, and line strips break at it (`IsSeparator()` in `shared/point.hlsl`).

## Step: Contour breaks in DrawLines

**Action:**
Add a `[LoadSvg]` operator (keep its default asset) and connect it to a
`[DrawLines]` operator.

**Expected:**
- Each contour of the SVG renders as its own line.
- No stray straight lines connect separate shapes or letters.

## Step: Contour breaks in DrawTubes and DrawRibbons

**Action:**
Replace `[DrawLines]` with `[DrawTubes]`, then with `[DrawRibbons]`.

**Expected:**
- Tubes and ribbons also break between contours — no tube/ribbon segment
  bridges from the end of one shape to the start of the next.

## Step: OBJ edges render as separate segments

**Action:**
Add a `[LoadObjAsPoints]` operator, set its Mode to line/edge output, and
connect it to `[DrawLines]`.

**Expected:**
- Mesh edges appear as separate segments.
- No extra lines connect unrelated edges.

## Step: Line breaks from WrapPointPosition

**Action:**
Build a chain of an animated point source (e.g. `[LinePoints]` moved by an
animated `[TransformPoints]`), a `[WrapPointPosition]` with **WriteLineBreaks**
enabled, and a `[DrawLines]`.

**Expected:**
- When points wrap around the volume edge, the line breaks there instead of
  streaking across the whole volume.

## Step: Separators are hidden in per-point draws

**Action:**
Connect the `[LoadSvg]` points from the first step to a `[DrawBillBoards]`
operator.

**Expected:**
- Billboards appear only at contour points.
- No stray billboard renders at the origin or between shapes (separator
  points are not drawn).

## Step: Empty text draws nothing

**Action:**
Add a `[TextSprites]` operator, clear its Text input to an empty string, and
connect it to `[DrawBillBoards]`.

**Expected:**
- Nothing renders; no single billboard appears at the origin.
