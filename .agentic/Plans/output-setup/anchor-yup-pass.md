# Anchor / Y-up normalization pass — conversion inventory (2026-09-05)

Implements [`data-model.md`](data-model.md) §5 and decision №7 in one coordinated pass: signed centered
anchors, surface-local space Y-up with its origin **at the anchor**, `Pivot` → `Anchor`. This file is the
inventory the pass was agreed to start with — every site that converts between the old and new
conventions, so the geometry is reviewed once, as a whole.

## Target conventions (from §5, restated for the code)

| | As built | Target |
|---|---|---|
| Anchor field | `StagePlacement.Pivot`, unsigned 0..1 from the surface's bottom-left | `StagePlacement.Anchor`, signed −1..1, center `(0,0)`, Y-up |
| Surface-local space | meters, origin **top-left**, Y **down** (matches quad winding TL,TR,BR,BL) | meters, origin **at the anchor**, Y **up** |
| Child `LocalPosition` | bottom-left of the child in meters from the parent's anchor, X right, Y up | **unchanged** — it is already in the target convention |
| `Annotations` (P1/P2) | surface-local (top-left, Y-down) | surface-local (anchor origin, Y-up) — converted on load |
| Raster origin | `GridOrigin.xy` = anchor in source UV (`pivot.X`, `1 − pivot.Y`) | same UV quantity, derived from the signed anchor |
| Output quads, slice UV, reference px | Y-down | unchanged (§5: pixels/UV stay Y-down) |

Conversions:
- anchor ← pivot: `a = 2p − 1` (both axes; the pivot was already bottom-left/Y-up in *normalized* terms).
- anchor position in meters from the surface's bottom-left: `ax = (a.X + 1)/2 · W`, `ay = (a.Y + 1)/2 · H`.
- old surface-local `(x, y_down)` → new `(x − ax, (H − y_down) − ay)`.
- The surface's own rectangle in new local space is `[−ax, W−ax] × [−ay, H−ay]`; its corners in quad
  winding are TL `(−ax, H−ay)`, TR `(W−ax, H−ay)`, BR `(W−ax, −ay)`, BL `(−ax, −ay)`.

**Why the origin moves to the anchor:** a crop then never moves the local origin — annotations and child
regions stay put by construction instead of being counter-moved (`ApplyRect`'s `pinAnnotations`,
`ResizeSurfaceCommand.State.Annotations` and the pivot counter-move all exist only to fake this today).

## Inventory — every flip / anchor site

### Core (`Core/Output/`)
| Site | Today | Change |
|---|---|---|
| `Surface.StagePlacement.Pivot` + JSON `"Pivot"` | 0..1 bottom-left | Rename to `Anchor`, write `"Anchor"`; reader accepts `"Anchor"`, else converts a legacy `"Pivot"` (`2p−1`) |
| `Surface.Annotations` xmldoc + JSON | top-left Y-down | Reader converts legacy lines using the surface's size and anchor (needs both, so it runs in `Surface.ReadFromJson` after size/placement are read) — keyed on the **absence of `"Anchor"`** in the placement, or on a new `Setup.CurrentVersion = 2` |
| `Surface.LocalPosition` | already target | Doc only |
| `Surface.ShowGrid` xmldoc | references `Pivot` | Wording |
| `LineAnnotation` xmldoc | "reference-image pixels" — stale for surface annotations | Wording |

### `SurfaceGeometry.cs` (the spine — rewrite as a unit)
| Member | Today | Change |
|---|---|---|
| class doc | "origin top-left, Y down" | New convention statement |
| `RectForSize(size)` | `[0,0]..[W,H]` Y-down | Becomes `LocalRect(surface)` — the anchor-relative rect above; quad-to-quad homographies now go `LocalRect ↔ Quad` |
| `RectFromBounds(min,max)` | TL,TR,BR,BL from Y-down bounds | Winding flips: with Y-up, TL = `(min.X, max.Y)`, BR = `(max.X, min.Y)` |
| `TryGetSurfaceToOutput` / `TryGetOutputToSurface` | via `RectForSize` | via `LocalRect` |
| `ApplyRect(surface, newRect, pinAnnotations)` | re-projects quads, then counter-moves the pivot and shifts annotations by `min` | re-projects quads; new size from bounds; **anchor becomes the origin's normalized position in the new rect**: `a.X = 2·(0 − min.X)/W' − 1`, `a.Y = 2·(0 − min.Y)/H' − 1`; annotations untouched; `pinAnnotations` parameter dies |
| `AnchorInSurface` | pivot → top-left Y-down meters | Dies: the anchor *is* the origin `(0,0)` |
| `ChildRectInParent` | anchor + `(x, −y)` then Y-down bounds | `min = LocalPosition`, `max = LocalPosition + size` (pure Y-up) |
| `SetChildRect` | inverse of the above | `LocalPosition = min`, size from bounds |
| `TryGetDescendantRect` | composes rects, `offset = min` (Y-down min = TL) | same composition; `offset = min` is now the child's bottom-left = its local origin, which is what the child's own children are relative to — **verify**: a child's local origin is *its anchor*, not its bottom-left, so nested levels must offset by `min + childAnchorMeters`. Today the parent-space origin of a child was TL because the child's local space started at TL; with anchor-origin spaces, the step is `offset = min + (child anchor in meters from its bottom-left)` |
| `TryGetChildQuad` | corner order from min/max | winding flip as in `RectFromBounds` |
| `CollectSnapCandidates` | parent edges at `0`, `W/2`, `W` and `0`, `H/2`, `H` | parent edges at `−ax`, `W/2−ax`, `W−ax` / `−ay`, `H/2−ay`, `H−ay` (i.e. `LocalRect` bounds) |
| `ResizeAnchored` | pivot math in Y-down | trivial now: `min = (−ax', −ay')` where `ax' = (a.X+1)/2·W'` — the anchor stays at the origin |
| `DragEdge(edge)` | 0 = top sets `min.Y`, 2 = bottom sets `max.Y` | edge indices are screen-winding (0 top … 3 left); with Y-up, top sets `max.Y`, bottom sets `min.Y`; the `keepDimensions` restore keeps the anchor instead of the pivot |

### `SetupOutputView.cs` (canvas; view space stays Y-down)
| Site | Today | Change |
|---|---|---|
| L160–180 straight-basis pivot freezing / `BlendBasisTransition` (`_basisFromPivot`, `_basisLastPivot`) | lerps pivot | lerps the anchor; rename fields |
| `AnchoredRect(refMin, refMax, pivot, size)` L1815 | places a view-space (Y-down) rect so the pivot coincides | same idea with `t = (a+1)/2`, Y flipped once (`anchorY = refMax.Y − t.Y·height`) |
| `DrawAnchorMarker` L1664 | recomputes pivot → surface meters | the anchor is `Vector2.Zero` in surface space: `surfaceToOutput.TransformPoint(Vector2.Zero)` |
| Region anchor glyph L1157 (`rectMin + AnchorInSurface(child)`) | | `rectMin` (child's bottom-left in carrier space) + child anchor meters from bottom-left |
| Region edge edit L1215–1222 (`case 0: min.Y = …`) | Y-down edge cases | same flip as `DragEdge` |
| `DrawSnapGuide` L1419 | extends the guide by `±size` in parent space | guide runs across `LocalRect(parent)` bounds ± size |
| `ToParentSpace` L1434 | `inCarrier − parentOrigin` | unchanged semantically once `TryGetDescendantRect` reports the parent's *anchor origin* |
| Label-move / `HandleChildEdit` / `RunResizeDrag` | go through `ChildRectInParent`/`SetChildRect` | no direct flips — **verify by test** |
| `SnapThresholds` (per-axis local thresholds) | derives meters-per-pixel from a probe offset | sign-agnostic (uses lengths) — **verify** |

### `SetupOutputView.Measure.cs`
| Site | Today | Change |
|---|---|---|
| `ToView` / `ToSurface` local functions | homography both ways | unchanged (the homography carries the convention) |
| `TryStraightenFromLines` L292–350 | re-projects other mappings through `RectForSize` | through `LocalRect` |
| `ScaleSurfaceMetric` L400 | scales size, annotations, `LocalPosition` from the top-left origin | scales from the anchor origin — with the anchor normalized, this is the same physical result; child recursion unchanged |
| `LineRectifier.IsHorizontal(P1, P2)` | angle only | sign-agnostic |

### `OutputManager.cs`
| Site | Today | Change |
|---|---|---|
| `gridOrigin = (pivot.X, 1 − pivot.Y)` L207 | UV of the anchor | `((a.X+1)/2, 1 − (a.Y+1)/2)`; shader unchanged |
| `SetAimPoint(surfaceId, inSurface)` L320 + crosshair L353 | surface-local in, projected via homography | unchanged |

### `SetupActions.cs`
| Site | Today | Change |
|---|---|---|
| `AddSurface` L360 | no placement → pivot `(0,0)` | no placement → anchor default (**decision A below**) |
| Region from slice L947–957 (`anchor`, `bottomLeft` in Y-down) | centered in parent | `LocalPosition = (W/2 − w/2, H/2 − h/2) − (ax, ay)` |
| `AddRegion` L1062–1073 (`(0.1W, 0.9H)` Y-down) | lands inside the parent | `LocalPosition = (0.1W − ax, 0.1H − ay)` |
| `DuplicateEntity` L337, L1026, L1043 | copies `LocalPosition`, `Pivot` | field rename only |

### Undo / repair / cards
| Site | Change |
|---|---|
| `ResizeSurfaceCommand.State` — `Pivot` field, `Annotations` snapshot | rename; the annotations snapshot can stay (harmless) but its comment ("a crop re-bases the frame") becomes false — drop the snapshot with the counter-move |
| `SetupSanitizer` L38–41 | rename; reset value for a non-finite anchor: `(−1,−1)` if decision A keeps bottom-left |
| `SetupParameterView` surface card: "Anchor (0..1)" field L278, "Position in parent (m)" tooltip L208 | label → **"Anchor (−1..1)"**, tooltip wording |
| `.tests-manual/output-setup-parameter-window.md` L45, `output-setup-panel-consistency.md` L117 | expected values: new surface shows Anchor `(−1 × −1)` (or `(0 × −1)` per decision A) |
| `data-model.md` §5 "As-built caveat" | delete once landed |

### Not affected (checked)
`Slice`/UV math (`SetupOutputView.Slices.cs`), `ReferenceImageView` and `ReferenceBinding` (px), `Homography`,
`ProjectorSolver`, `LineRectifier` (operate on quads/px), `Pose` (already Y-up), `OutputMapping.Quad`.
No tests reference `SurfaceGeometry`, `Pivot` or `LocalPosition` — the pass should add a round-trip test
for the legacy-`Pivot` read and a `SurfaceGeometry` crop test (anchor stays at the origin, annotation
untouched).

## Status

**Landed 2026-09-05.** Decisions: **A = bottom-centre `(0,−1)`** default; **B = no legacy conversion**
(the format had never left internal preview — existing files simply read the default anchor and keep
their annotation numbers, which are re-interpreted in the new space). One deviation from the table
above: the anchor lives on **`Surface.Anchor`** directly, not on `StagePlacement` — it is a property of
the rectangle, and a placement no longer has to be created just to hold a crop's re-derived anchor.
`ResizeSurfaceCommand.State` lost its annotation snapshot for the same reason.

## Decisions (as settled)

- **A. Default anchor for a new surface.** Old behaviour is bottom-left (`Pivot (0,0)` → anchor `(−1,−1)`).
  §5 names bottom-center `(0,−1)` as the floor-standing default for Phase C placement. The raster starts at
  the anchor, so bottom-center means the meter lines are centered on the surface rather than starting at
  its left edge.
- **B. Legacy detection.** Per-field (a placement with `"Pivot"` but no `"Anchor"`, plus annotations
  converted whenever the placement was legacy or absent) vs. bumping `Setup.CurrentVersion` to 2 and
  converting everything under `version < 2`. Version bump is one clear switch and also covers surfaces
  with no placement at all (their annotations still need the top-left → anchor conversion).
