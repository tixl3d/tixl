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
All separator producers and consumers use one convention (`Scale.x` = NaN); a
NaN width (`FX1`) additionally hides individual points in width-based draw ops.

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

## Step: Sprite hiding via NaN width still works

**Action:**
Add a `[TextSprites]` operator with a multi-word text and connect it to
`[DrawBillBoards]`.

**Expected:**
- Only visible glyphs render; no billboard quads appear at whitespace
  positions or at the origin.
