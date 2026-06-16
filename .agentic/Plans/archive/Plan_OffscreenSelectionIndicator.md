# Off-Screen Selection & Hover Indicator (Graph Edge Markers)

Feature: when the current selection — or a hovered operator — lies outside the visible graph area, draw a thin marker pinned to the nearest edge of the graph window, "projecting" the off-screen box's position onto that edge. Lets the user find a selected/highlighted operator that has scrolled out of view without changing zoom or pan. Inspired by Figma's off-canvas selection hint.

## Goals

- Show **where** an off-screen selection or hovered operator is, relative to the visible area, as a screen-space edge marker — without moving the view.
- Be **non-destructive**: this is a passive wayfinding hint, complementary to the existing `F` "fit view to selection" (which *does* move the view). It never steals focus or interaction.
- **Allocation-free.** Iterate the (small) selection list and hovered-id set directly and reuse the already-computed `_visibleCanvasArea`. No LINQ, no per-frame heap traffic (the editor draws this every frame at output refresh rate).
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
| Overlay draw pass (new) | `Editor/Gui/MagGraph/Ui/MagGraphCanvas.OffscreenIndicators.cs` (new partial) | `DrawOffscreenIndicators(drawList)` + `DrawEdgeMarker(...)` + `GetClampedSpan(...)` / `AccumulateBounds(...)` helpers. Builds selection bounds by iterating `_context.Selector.Selection`, hover bounds by iterating `FrameStats.Last.HoveredIds` → `Layout.Items` lookup. Allocation-free. |
| Hook call | [MagGraphCanvas.Drawing.cs:161](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.Drawing.cs:161) | Single `DrawOffscreenIndicators(drawList)` call right after the connection loop. |
| Screen transform | [ScalableCanvas.cs:152](../../Editor/Gui/Interaction/ScalableCanvas.cs:152) `TransformRect` / `WindowPos` / `WindowSize` | Read-only use; no change. |
| Hover source | [FrameStats.cs:79](../../Editor/Gui/FrameStats.cs:79) `Last.HoveredIds`, [Drawing.cs:459](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.Drawing.cs:459) `Blink` | Read-only use; no change. |
| Selection source | [NodeSelection.cs:334](../../Editor/UiModel/Selection/NodeSelection.cs:334) `Selection` (via [GraphUiContext.cs:80](../../Editor/Gui/MagGraph/States/GraphUiContext.cs:80) `Selector`) | Read-only use; no change. |

All changes are in `Editor/` — one new file plus a one-line call. **No `Core/` changes, no `DrawNode` changes, no new persistent state.** No overlap with the in-flight Snapshot/Variation work (`SnapshotControlView.cs`, `VariationPicker.cs`, `CustomComponents.Menus.cs`).

> **Why not accumulate inside the `DrawNode` loop (as first sketched).** `DrawNode` early-returns at [DrawNode.cs:32](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs:32) — `if (!IsRectVisible(item.Area)) return;` — *before* the selection/highlight code. Off-screen items (exactly the ones this feature targets) never reach it. So bounds are built from the selection list and hovered-id set instead, both small and allocation-free to iterate.

---

## Phase Plan

Each phase compiles and is independently shippable. `dotnet build Editor/Editor.csproj` must pass at the end of each.

### Phase 1 — Selection edge marker — **done**

A steady marker appears at the nearest edge when the selection is fully off-screen, with the edge/corner blend and appear-fade. Implemented in `DrawOffscreenIndicators` / `DrawEdgeMarker`:

- selection bounds built by iterating `_context.Selector.Selection` (`ImRect.RectWithSize(s.PosOnCanvas, s.Size)`), allocation-free;
- early-out via the existing `IsRectVisible(bounds)` (canvas-space `_visibleCanvasArea.Overlaps`);
- box transformed to screen via `TransformRect`; window rect from `WindowPos`/`WindowSize`, inset by padding;
- `GetClampedSpan` clamps the projected extent into the inset window and enforces the min leg length (edge→corner is one continuous rule);
- alpha ramps by distance-past-edge; drawn with `UiColors.ForegroundFull`;
- **all sizes × `T3Ui.UiScaleFactor`, never `CanvasScale`** — the marker is a zoom-independent HUD element.

### Phase 2 — Hover marker (blinking) — **done**

- hover bounds built by iterating `FrameStats.Last.HoveredIds` → `Layout.Items.TryGetValue(id, …)` → `item.Area`;
- gated on `!IsHovered`, mirroring the on-node highlight condition (so it's the cross-panel hover case);
- drawn with `UiColors.ForegroundFull.Fade(Blink)` — the same blink as the on-node highlight;
- shares `DrawEdgeMarker`, so the distance-fade compounds with the blink.

### Phase 3 — Tuning & polish (open)

The marker is drawn with **one unified model** (no per-edge modes — earlier mode-based drawing caused jumps at every edge/corner boundary): the box's four corners are projected onto the rounded window border as arc-length positions (`BorderArcLength`), the smallest arc containing them is taken (drop the largest gap between sorted positions), and that span is stroked by walking the border (`BorderPointAt` → `PathStroke`, rounded end-caps). The span slides along edges and wraps the rounded corners as one continuous function of the selection's position; a too-short span grows symmetrically along the border (no inward snap). Constants live at the bottom of the new partial (`EdgePadding 3`, `MarkerThickness 2`, `MarkerMinLength 14`, `CornerRadius 8`, `FadeInDistance 50`). Remaining:

1. Tune the constants against real graphs at several zoom levels (run the editor, observe).
2. Confirm the marker insets clear existing edge chrome (the faded left-edge `GraphOpacity` region; any toolbars).
3. Decide persistent-vs-fading for selection and the optional settings toggle (see Open Questions — currently persistent + always-on).
4. Add the manual test set; update `.help/` if a graph-navigation page warrants a mention.
5. Selection currently covers operator/annotation items in the selection list. A coincident selection+hover marker on the same edge uses the same inset (overlap is acceptable since both are `ForegroundFull`; revisit if the blink reads poorly under a steady marker).

---

## Implementation notes

- **Build bounds from the small sets, not `GetSelectionBounds`.** [`NodeSelection.GetSelectionBounds`](../../Editor/UiModel/Selection/NodeSelection.cs:245) uses `.ToArray()` + LINQ — fine for the user-triggered `F` framing, **not** for a per-frame overlay. Instead iterate `_context.Selector.Selection` (selection) and `FrameStats.Last.HoveredIds` (hover) directly — both small, both allocation-free (the `HashSet<Guid>` `foreach` uses its struct enumerator).
- **Selection uses `s.PosOnCanvas`/`s.Size`** (model position), hover uses `item.Area` from the `Layout.Items` lookup. Model position is fine — the marker points to roughly where the op sits; nodes don't move during canvas scroll, only during drag.
- **Single draw list, drawn after connections** so markers sit on top; no channel split needed (markers aren't clickable in v1).

---

## Open Questions

1. **Persistent vs. fading selection marker.** Recommended: persistent but subtle (thin, modest alpha) while off-screen — matches Figma and is the steady wayfinding aid the user asked for. Alternative: fade the selection marker out a few seconds after the selection last changed, to reduce long-lived edge clutter. Decision affects Phase 1 alpha logic.
2. **Marker color for selection.** Recommended: `UiColors.ForegroundFull`, matching the on-node selection outline (selection isn't a "status", so no `Status*` hue). Confirm.
3. **Click-to-focus follow-up?** Making the marker a thin edge-aligned `InvisibleButton` that calls `FocusViewToSelection()` / `FitAreaOnCanvas(...)` turns the hint into navigation (Figma-like). Cheap, but adds hit-testing — ship as a fast-follow after v1 feels right, or include in v1?
4. **Optional global toggle.** Add a `UserSettings` flag to disable the indicator, or always-on? Lean always-on (it's subtle and self-hiding) unless testing says otherwise.

---

## Manual test set

Added: [`.tests-manual/offscreen-selection-indicator.md`](../../.tests-manual/offscreen-selection-indicator.md) (scope `graph-window`, `added-in-version: 4.3`). Covers: selection marker off one edge; corner stub past two edges; clearing when back in view; zoom-independent sizing; and the blinking hover marker driven from another panel (Console / Timeline / Variations).

---

## Related Documents

- `Plan_MultiViewport.md` — graph rendering across multiple viewports; the indicator is per-`MagGraphView` so it works per-viewport unchanged.
- `.agentic/AGENT_INSTRUCTIONS.md` — per-frame allocation rules; screen-space scaling (`UiScaleFactor` vs `CanvasScale`); status-color meanings.

## Status

Implemented and accepted by the user — selection + hover markers, riding the rounded window border with smooth continuous corner wrapping. `Editor.csproj` compiles clean; verified live via hot reload. Manual test set added. Not committed yet (left to the user).

Deferred follow-ups (not requested, recorded for later): click-to-focus on the marker (reuse `FocusViewToSelection`); a `UserSettings` on/off toggle; optionally keep a straight dominant-edge leg at diagonal corners instead of the small arc-bracket. The reusable border helpers (`BorderArcLength` / `BorderPointAt`) could back other off-screen indicators (playback cursor, erroring op). No dependency on the in-flight Snapshot/Variation work.
