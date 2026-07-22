# Straighten interaction — slice plan

Goal: place axis-aligned rect surfaces on a projected wall **without a reference photo**, by
projecting a real-world calibration raster and hand-aligning the corner-pin to physical features.
This delivers the reusable 2D canvas toolkit (edges/crop/regions/snap) early and de-risks perspective
mapping, while shipping a legitimate standalone calibration method.

## Locked decisions (2026-07-22)
- **Crop gesture** = plain edge-handle drag; **Ctrl** = scale (matches the original edge-dragging spec).
- **Sub-regions** = child `Layout` surfaces in the existing `Surface` tree (`ParentId` + `Kind`), sharing
  the parent's mapping. No new entity, no migration.
- **First slice is thin** (this doc). Crop / regions / duplicate / snapping are the *next* slice.

## Two edit spaces (spine for the toolbar)
- **Original** — projector-pixel space; drag the 4 corner-pin corners (manual keystone). This is where you
  eyeball the projected grid straight against the wall.
- **Straight** — the rectified content canvas (axis-aligned); crop + regions edit here and map back through
  the *locked* corner-pin. `Straight` is a view/edit mode, a peer tab to `Original`, not a one-shot action.

## Status
- **Thin slice LANDED (2026-07-22, uncommitted, builds green):** all three chunks below done. Grid emitted
  through the corner-pin composite (shader `GridParams`/`GridColor`, analytic AA lines); surface card has
  Show size raster + Link cell size + Cell size (cm). **Original↔Straight is a continuous morph, not a mode
  switch:** `DrawOutputCanvas` carries a global rectify transform R driven by an animated `_straightenT`
  (0 = identity/Original, 1 = focused surface straightened to its axis-aligned bbox), framed to the surface's
  bbox + margin. Composite warped via `RenderWarpedTexture` while rectifying; all surfaces stay editable
  (reuse `CornerPinHandles` + undo; drags map back through R⁻¹; R frozen to the drag-start quad when the
  focused basis is the one dragged). `DrawStraightCanvas` deleted. NOT the full sketch toolbar (deferred).
  Note: content is digitally on the quad, so the editor view always renders straight — corner editing
  repositions/compensates the projection, judged on the wall.

## Thin slice = grid + size + Straight view
1. **Projected calibration grid** (this chunk): `Surface.ShowGrid` + `GridCellSize` (m) + `GridCellLinked`.
   The corner-pin composite shader draws an analytic grid (crisp, AA via `fwidth`) when a grid draw-item is
   emitted; cell counts = `SizeInMeters / GridCellSize`. Grid draws for `ShowGrid` surfaces even with no
   content sink. v1: grid is opaque (black between lines = unlit wall); overlay-on-content is a later option.
2. **Surface size editing** with proportional link (this chunk): cell size shown in cm, default 25×25, link on.
3. **Original / Straight toggle** (next chunk): toolbar tabs + a read-only rectified render of the surface
   canvas, reusing `RRenderWarpedTexture` / the inverse homography from the reference-image straighten.

## Next slices (not now)
- Edge-handle crop (plain drag) + Ctrl-scale; content-extent semantics.
- Sub-region (child Layout surface) create / duplicate (Ctrl+D, Alt-drag center) / edit.
- Snapping to sibling edges/corners on the canvas.
- Full toolbar per the sketch (Stage/Output · projector ▾ · Original/Straight/Surface · Straighten/Apply Lengths).
