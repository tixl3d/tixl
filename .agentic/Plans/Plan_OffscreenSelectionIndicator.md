# Off-Screen Selection & Hover Indicator (Graph Edge Markers)

Feature: when the current selection — or a hovered operator — lies outside the visible graph area, draw a thin marker pinned to the nearest edge of the graph window, "projecting" the off-screen box's position onto that edge. Lets the user find a selected/highlighted operator that has scrolled out of view without changing zoom or pan. Inspired by Figma's off-canvas selection hint.

## Goals

- Show **where** an off-screen selection or hovered operator is, relative to the visible area, as a screen-space edge marker — without moving the view.
- Be **non-destructive**: this is a passive wayfinding hint, complementary to the existing `F` "fit view to selection" (which *does* move the view). It never steals focus or interaction.
- **Allocation-free, no extra iteration.** Reuse the existing per-item `DrawNode` loop and the already-computed `_visibleCanvasArea`. No LINQ, no per-frame heap traffic (the editor draws this every frame at output refresh rate).
- Read consistently with the editor: reuse `UiColors.ForegroundFull` and the shared `Blink` sine; markers smoothly fade/blend rather than pop.

## Non-Goals (initial release)

- No indicator while the box is even partially visible. If any part of the bounding box overlaps the view area, draw nothing — the user can already see where it is. (Keeps large selections from cluttering the edges.)
- No click-to-focus on the marker initially (see Open Questions — cheap follow-up, deferred so v1 stays a pure overlay with no hit-testing).
- No directional chevron/arrow glyphs initially — a thin edge segment + corner stub carries the information; revisit only if testing shows the direction is ambiguous.
- No minimap. This is an edge hint, not a thumbnail of the whole graph.
- Legacy Graph is out of scope. MagGraph only.

---

## The projection model (the core design)

Three conceptual rectangles, all initially in **canvas space**:

1. **View area** — the visible canvas region. Already computed every frame as `_visibleCanvasArea` ([MagGraphCanvas.Drawing.cs:50](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.Drawing.cs:50)). In *screen* space this is just the graph window rect (`WindowPos` / `WindowSize`), so we never draw rectangle (1) — it's the edge the markers pin to.
2. **Selection bounding box** — union of the selected items' canvas rects.
3. **Hover bounding box** — union of the canvas rects of operators currently hovered from another panel (`FrameStats.IsIdHovered`).

For (2) and (3), if the box does **not** overlap (1), draw a marker. Rather than treat "edge segment" and "corner bracket" as two states to cross-fade, use **one** rule that makes the blend fall out of the geometry:

> **Clamp the box into the padding-inset viewport, then draw the clamped rect's pinned edges, enforcing a minimum leg length (~10–12 px screen).**

Work in **screen space** (transform the box's canvas rect via `TransformRect`, compare with the window rect):

- Box entirely **above** → clamps to a zero-height segment on the top edge; its x-range is the clamped horizontal overlap → a horizontal marker whose length and position track the box. (Same logic mirrored for bottom/left/right.)
- Box **above-and-left** → clamps to a near-zero-size point in the top-left corner; the per-axis minimum length kicks in → an L-shaped corner stub.
- As the user pans from "directly above" toward "above-left", the top segment shrinks continuously toward the corner while the left leg grows in. **No discrete state machine, no cross-fade — the blend is the clamp.**

**Appear/disappear smoothing.** The `Overlaps → nothing` rule pops at the boundary. Ramp marker *alpha* by distance-past-edge over the first few screen px, so a box sliding out of view fades the marker in instead of snapping it on. (Panning itself is already damped via `ScrollTarget` / `DampScaling`.)

> See the diagram in the planning conversation for the three cases (edge marker, corner stub, blinking hover).

---

## Selection vs. hover

| | Selection marker | Hover marker |
|---|---|---|
| Source | items where `Selector.IsSelected(item)` | items where `FrameStats.IsIdHovered(item.Instance.SymbolChildId)` (the cross-panel hover that already drives the on-node highlight) |
| Color | `UiColors.ForegroundFull` steady | `UiColors.ForegroundFull.Fade(Blink)` — blinking |
| Shown when | selection is fully off-screen | something is hovered *and* off-screen |
| Inset from edge | e.g. 2 px | a slightly different inset so the two don't z-fight when they land on the same edge |

The hover case is the strongest justification: TiXL **already** draws a blinking highlight on a node hovered from another panel ([MagGraphCanvas.DrawNode.cs:152](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs:152)) — but it's invisible exactly when the node is off-screen, which is when you most need it. This feature just relocates that existing blink to the edge. Selection is steady (not blinking) so a persistent off-screen selection isn't an irritant.

Multiple hovered/selected items → union into one box per kind (cheap, and avoids a thicket of markers).

---

## Affected Systems — Impact Summary

| System | File | Change |
|---|---|---|
| Per-item bounds accumulation | [MagGraphCanvas.DrawNode.cs:144-159](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs:144) | Inside the existing loop, where `isSelected` / `isHighlighted` are already known, accumulate two `ImRect` bounds + "any" flags. Zero extra iteration. |
| Overlay draw pass | [MagGraphCanvas.Drawing.cs:157-161](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.Drawing.cs:157) | After the connection loop, draw the edge markers from the accumulated bounds + `_visibleCanvasArea` / window rect. New private `DrawOffscreenIndicators(...)` partial method. |
| Frame-local accumulators | [MagGraphView.cs:220](../../Editor/Gui/MagGraph/Ui/MagGraphView.cs:220) (near `_visibleCanvasArea`) | Add private fields: `_selectionBounds`, `_hoverBounds`, `_anySelectionBounds`, `_anyHoverBounds`; reset at frame start (next to where `_visibleCanvasArea` is set). |
| Screen transform | [ScalableCanvas.cs](../../Editor/Gui/Interaction/ScalableCanvas.cs) `TransformRect` / `WindowPos` / `WindowSize` | Read-only use; no change. |
| Hover source | [FrameStats.cs:39](../../Editor/Gui/FrameStats.cs:39) `IsIdHovered`, [Drawing.cs:459](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.Drawing.cs:459) `Blink` | Read-only use; no change. |

All changes are in `Editor/`. **No `Core/` changes.** No new state outside the graph view. No overlap with the in-flight Snapshot/Variation work (`SnapshotControlView.cs`, `VariationPicker.cs`, `CustomComponents.Menus.cs`).

---

## Phase Plan

Each phase compiles and is independently shippable. `dotnet build Editor/Editor.csproj` must pass at the end of each.

### Phase 1 — Selection edge marker

Goal: a steady marker appears at the nearest edge when the selection is fully off-screen, with the edge/corner blend and appear-fade.

1. Add the frame-local accumulator fields near `_visibleCanvasArea` and reset them where `_visibleCanvasArea` is assigned ([Drawing.cs:50](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.Drawing.cs:50)).
2. In `DrawNode`, where `isSelected` is already computed ([DrawNode.cs:144](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs:144)), `Add(item.Area)` into `_selectionBounds` and set `_anySelectionBounds = true`. (Use `item.Area`, the canvas rect — not the clamped on-screen `pMinVisible/pMaxVisible`.)
3. Add `DrawOffscreenIndicators(drawList)` as a new partial, called after the connection loop ([Drawing.cs:161](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.Drawing.cs:161)). It:
   - early-outs if `!_anySelectionBounds` or `_selectionBounds.Overlaps(_visibleCanvasArea)`;
   - transforms `_selectionBounds` to screen via `TransformRect`;
   - computes the window screen rect (`ImRect.RectWithSize(WindowPos, WindowSize)`), inset by padding × `T3Ui.UiScaleFactor`;
   - clamps the screen box into the inset window rect, applies the min-leg length, and draws the marker leg(s) with `drawList.AddRectFilled` in `UiColors.ForegroundFull`;
   - ramps alpha by distance-past-edge.
4. **Thickness/inset/min-leg are screen pixels → multiply by `T3Ui.UiScaleFactor`, NOT `CanvasScale`.** This is the easy-to-miss gotcha: the marker is a HUD element, independent of zoom.

**Deliverable:** select an operator, scroll it off-screen → a steady edge marker tracks its direction; scroll it past a corner → a corner stub; scroll it back into view → marker fades out.

### Phase 2 — Hover marker (blinking)

1. In `DrawNode`, where `isHighlighted` is already computed ([DrawNode.cs:152](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs:152)), `Add(item.Area)` into `_hoverBounds` / set `_anyHoverBounds`.
2. In `DrawOffscreenIndicators`, after the selection marker, draw the hover marker from `_hoverBounds` with `UiColors.ForegroundFull.Fade(Blink)` and a slightly different edge inset so it doesn't z-fight a coincident selection marker.
3. Verify the cross-panel hover path: hover an operator's row in the Parameter window / a connection endpoint while the op is scrolled off-screen → blinking edge marker points to it.

**Deliverable:** the "locate this operator" affordance now works when the target is off-screen — the feature's primary payoff.

### Phase 3 — Tuning & polish

1. Tune padding, min-leg length, marker thickness, alpha-ramp distance, and corner stub leg length against real graphs at several zoom levels.
2. Confirm the marker insets clear existing edge chrome (the faded left-edge `GraphOpacity` region; any toolbars).
3. Decide persistent-vs-fading for selection (see Open Questions) and the optional settings toggle.
4. Add the manual test set; update `.help/` if a graph-navigation page warrants a mention.

**Deliverable:** shippable.

---

## Implementation notes

- **Why accumulate in `DrawNode`, not call `GetSelectionBounds`.** [`NodeSelection.GetSelectionBounds`](../../Editor/UiModel/Selection/NodeSelection.cs:245) uses `.ToArray()` + LINQ — fine for the user-triggered `F` framing, **not** for a per-frame overlay. The `DrawNode` loop already visits every item and already knows `isSelected`/`isHighlighted`, so accumulating `item.Area` there is free and allocation-free. Bounds also stay correct while item positions are damped/animated, since they're recomputed each frame from live positions.
- **Use `item.Area`** (`ImRect.RectWithSize(PosOnCanvas, Size)`, [MagGraphItem.cs:42](../../Editor/Gui/MagGraph/Model/MagGraphItem.cs:42)) for canvas-space bounds — not the clamped visible screen corners.
- **Static buffers** for any small point arrays in the draw, per the per-frame draw rules.
- **Single draw list, drawn last** (after connections) so markers sit on top; no channel split needed (markers aren't clickable in v1).

---

## Open Questions

1. **Persistent vs. fading selection marker.** Recommended: persistent but subtle (thin, modest alpha) while off-screen — matches Figma and is the steady wayfinding aid the user asked for. Alternative: fade the selection marker out a few seconds after the selection last changed, to reduce long-lived edge clutter. Decision affects Phase 1 alpha logic.
2. **Marker color for selection.** Recommended: `UiColors.ForegroundFull`, matching the on-node selection outline (selection isn't a "status", so no `Status*` hue). Confirm.
3. **Click-to-focus follow-up?** Making the marker a thin edge-aligned `InvisibleButton` that calls `FocusViewToSelection()` / `FitAreaOnCanvas(...)` turns the hint into navigation (Figma-like). Cheap, but adds hit-testing — ship as a fast-follow after v1 feels right, or include in v1?
4. **Optional global toggle.** Add a `UserSettings` flag to disable the indicator, or always-on? Lean always-on (it's subtle and self-hiding) unless testing says otherwise.

---

## Manual test set

To be added with the implementation PR as `.tests-manual/offscreen-selection-indicator.md` (frontmatter `added` / `added-in-version` per [`.tests-manual/README.md`](../../.tests-manual/README.md)). Outline:

- Select an operator, scroll it off the top → a steady marker appears on the top edge, tracking its horizontal position; scroll back → it fades out.
- Scroll the selection past the top-left corner → the marker becomes an L-shaped corner stub.
- With nothing off-screen visible, hover the selected op's row in the Parameter window while it's scrolled away → a blinking marker points to it.
- Selection + hover land on the same edge → both readable (steady vs. blinking), not overlapping into one blob.
- Zoom to several levels → marker thickness/inset stay constant in screen pixels (DPI/zoom independent).

---

## Related Documents

- `Plan_MultiViewport.md` — graph rendering across multiple viewports; the indicator is per-`MagGraphView` so it works per-viewport unchanged.
- `.agentic/AGENT_INSTRUCTIONS.md` — per-frame allocation rules; screen-space scaling (`UiScaleFactor` vs `CanvasScale`); status-color meanings.

## Status

Draft — not yet implemented. Phase 1 is self-contained and safe to start; no dependency on the in-flight Snapshot/Variation work.
