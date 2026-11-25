# Connection Routes

**Status (2026-11-25):** phase 1 built and first user test done; route points made selectable afterwards (uncommitted, on `main`). Modifier is `Alt`
(briefly `Shift`; kept `Alt` since most users pan with MMB/RMB/trackpad).
Manual test set: [`graph-connection-routes`](../../.tests-manual/graph-connection-routes.md).

**Goal:** let users route a connection line around the operators it crosses by adding route points to it,
and let connections from the same output share route points so they run along a common trunk. Purely a
display feature: evaluation and the graph structure don't change, and it works for every data type.

## Background

Two community PRs tried this:

- **#1171** (closed) added one reroute operator per data type. That needed ~40 duplicated ops and was dropped
  in favour of a type-agnostic approach.
- **#1196** (open, "Bend point") stored per-connection waypoints on the source child. Its interaction and
  drawing work, but the data model has problems this plan fixes:
  - keyed by `(SourceOutputId, TargetInputId)`, which are slot *definition* ids, so every instance of the same
    symbol fed by one output shares the same bend points;
  - absolute canvas positions that never move with the operators;
  - no cleanup when a connection or operator is deleted, so stale routes reappear.

We also considered **reroute dots** as real canvas items (Blender/Unreal style). They make moving, copying and
fanning out natural, but they touch MagGraph layout, snapping, selection and connection resolution. Shared route
points give the trunk without that risk.

## Data model

Owned by the composition's `SymbolUi` as `ConnectionRoutes` (`Editor/UiModel/ConnectionRoutes.cs`):

- `Points: Dictionary<Guid, Vector2>` — route point id → position on canvas.
- `Routes: Dictionary<Key, List<Guid>>` — connection → ordered point ids from source to target.
- `Key` is a value struct of the four ids of a `Symbol.Connection`
  (`SourceParentOrChildId, SourceSlotId, TargetParentOrChildId, TargetSlotId`). It identifies the connection
  itself, not a slot definition, so instances never collide. Two connections from the same output into one
  multi-input share a key and therefore a route; that's acceptable (rare, and they start at the same output).

Rules:

- **Trunks:** only routes with the same source (`SourceParentOrChildId`, `SourceSlotId`) may share a point.
  Otherwise one "trunk" would visually merge two different signals.
- **Lifetime:** a point exists while at least one route references it. Routes of connections that no longer
  exist stay in memory during the session, so undoing a deletion brings the route back. They are invisible
  (drawing only looks up live connections) and are dropped on save.
- **Positions** are absolute canvas positions.
- **Selectable:** each point is a `ConnectionRoutes.RoutePoint : ISelectableCanvasObject`, returned by
  `SymbolUi.GetSelectables()` (live routes only). So the user decides what moves: fence and Shift+G select points
  with the operators, selected points follow node drags (in the drag's undo step, offset taken from a dragged
  operator so snapping is included), section drags take the points inside the section, and `Delete` removes them.
  Unselected points stay put and their cables stretch. (Phase 1 first moved a point only when both ends of every
  route using it were dragged; that broke as soon as other moves were involved.) Snapshot undo updates
  `RoutePoint` objects in place so a selection holding them stays valid.

### File format

A new optional top-level block in `.t3ui`, ignored by older editors:

```json
"ConnectionRoutes": {
  "Points": [ { "Id": "…", "Position": { "X": 0, "Y": 0 } } ],
  "Routes": [ { "SourceParentOrChildId": "…", "SourceSlotId": "…",
                "TargetParentOrChildId": "…", "TargetSlotId": "…", "PointIds": [ "…" ] } ]
}
```

Objects (not bare arrays) leave room for later per-point or per-route fields (style, label, junction flag).
Written sorted by id for stable diffs; only routes of existing connections and referenced points are written.

## Undo

`ModifyConnectionRoutesCommand` snapshots the composition's whole `ConnectionRoutes` before and after an edit.
Route data is small, and one snapshot command covers every edit: add, move, merge, remove, and points that follow
a node drag (added to the drag's macro command).

## Interaction (MagGraph only)

| Gesture | Result |
|---|---|
| `Alt` + click on a cable | Adds a route point there and starts dragging it |
| Drag a route point | Moves it (all cables sharing it follow) |
| Drop a point on a point of another cable from the same output | Merges them into one shared point (trunk) |
| `Alt` + click on a route point (no movement) | Removes it from every cable that uses it; moving before release drags it instead |
| Click / Shift+click a route point | Selects it / adds it to the selection |
| Drag a selected point | Moves all selected points |
| Shake a dragged point | Removes it |
| `Delete` with points selected | Removes them |
| `Esc` while dragging | Cancels |

`Alt` also pans the canvas on left-drag. A click on a cable or route point starts the route drag in the same frame,
before the pan threshold is reached, and panning is switched off while a point is dragged
(`MagGraphView.PansWithAltDrag`). The cost: an `Alt`-pan that starts on a cable adds a point instead (accepted; most
users pan with MMB, RMB or the trackpad). `Alt`+click on a point removes it only on release without movement, so an
`Alt`-pan starting on a point moves it rather than deleting it. While `Alt`+click would add a point, the cable's
hover circle grows to 1.5×.

Route points draw as small rings on the cable, filled when hovered or dragged, hidden when zoomed far out.
Dragging runs in its own `GraphStates.DragRoutePoint`, so the selection fence and node clicks stay out of it.
A click on a route point is claimed before `ConnectionHovering` can turn it into "drag the connection end".

## Drawing

A routed cable is built in the draw list's path (no managed allocation) from the source anchor through each point
to the target input. Between horizontal ends each segment uses `GraphConnectionDrawer.AppendArcPath`, the same
horizontal-runs-and-arcs shape as unrouted lines; segments that leave or enter vertically fall back to a Bézier. Hover testing walks the path segments (the generic helper
assumes the cable stays between its endpoints, which a detour doesn't). Snapped connections and connections into
or out of collapsed sections ignore their route and draw as usual.

## Phases

1. **Core** (this change): data model + JSON, snapshot command, drawing + hover, add/move/remove/merge,
   points follow group drags, copy/paste/duplicate (routes copy when both ends are copied, with new point ids
   and the paste offset), help page, release note, manual test set.
2. **Snapping and polish** (after user testing):
   - ✅ **Vertical snapping** while dragging a point: its Y snaps to the output and input of every line through it
     and to those lines' other points that stay in place (collected at drag start), shown with a
     `ValueSnapHandler` snap line. `Ctrl` bypasses it; a merge target takes precedence.
   - **Horizontal snapping** (maybe): snap X to route points of other connections, to line up parallel cables.
   - Detach one cable from a shared point (modifier + drag), a junction marker where branches split.
   - Hover/insertion for the `BottomToLeft`/`RightToTop` Bézier styles (they have no hover test today).
3. **Maybe:** "Straighten" context-menu action that removes all routes of the selection; legacy graph support
   (not planned, legacy graph is being retired).

## Risks

- **Per-frame cost:** one dictionary lookup per drawn connection, skipped entirely when the composition has no
  routes. Routed cables cost about the same as normal ones.
- **Stale routes in memory** until save: harmless, but reconnecting the exact same output→input in the same
  session brings the old route back (also what makes undo work).
- **Layout tools** (tree layout, snapping, splice) don't know about routes; they move nodes and the cables
  stretch. Acceptable for phase 1.
