# Output Setup — Selection & Sub-element Editing

Selection is cross-cutting (sidebar tree, canvas handles, property panel, drag-drop, keyboard) and the spec
needs **multi-select down to sub-elements** — corners of *several* slices at once, annotation endpoints,
calibration-lattice points. Deferring this is dangerous (it entangles keyboard + drag semantics late), so
the model is fixed here up front. Selection itself is **never undoable** (spec undo policy).

## Core idea: two planes that never mix

The thing that keeps multi-select tractable is refusing heterogeneous selections. There are two independent
selection sets:

| Plane | Contains | Drives | Owner class |
|---|---|---|---|
| **Entity** | whole entities — Surface, Region, Slice, Output, ReferenceImage, Prop, ContentSource | property panel, tree highlight, entity **drag-drop** (callout 21) | `SetupEntitySelection` (extended) |
| **Sub-element** | handles — mapping/slice **Corner**, **Annotation** endpoint, **LatticePoint** | on-canvas dragging, per-corner color, lattice tweak | new `CanvasSelection` |

You never hold "2 surfaces + 3 corners" in one set. That single rule is what makes shift-add, box-select,
arrow-nudge, and drag behave predictably.

## Selection scope across windows (2026-08-29)

The entity plane is **one shared instance across all output windows** (and the graph / Parameter
window) — not per-window as first built. Rationale: the Parameter window needs exactly one inspected
item with no "which window feeds me" rule; cross-window highlight sync comes free; and the one-window
case (the common one) is indistinguishable from before.

What made per-window selection necessary was that selection also decided *what a window displays*.
That coupling is removed instead of kept: a window **follows** the shared selection by default, and
can be **pinned** to an entity/output via its breadcrumb — it then keeps showing the pinned target
while selection roams (calibrate on projector A while clicking around B's entities). **Pinning ships
in the same slice as the sharing** — sharing without pinning makes a second output window useless.

Consequences:
- `SetupEntitySelection` becomes one shared instance; windows hold only their pin state
  (kind + id, cleared via the breadcrumb).
- The **sub-element plane is shared too**: it is populated from the selected entity, so it follows
  the selection, not the window. A pinned window showing a different canvas simply has no selected
  handles to draw. Its clear rule reads "the *selected* entity's canvas context changed" — not "this
  window switched views".
- The graph-selection invariant (an op selection clears/replaces the entity selection) is unchanged;
  it now applies to the one shared set.

## One address form

Both planes address targets the same way:

```
readonly struct SelectionTarget {
    EntityKind Kind;      // ReferenceImage | Surface | Slice | Output | Prop | ContentSource
    Guid       EntityId;  // the owning entity
    SubPart    Part;      // None | Corner | Annotation | LatticePoint
    int        Index;     // corner 0..3, annotation index, lattice point index; -1 when Part==None
}
```

- Entity plane: `Part == None`.
- Sub-element plane: `Part != None`, `EntityId` = the owner (e.g. the Slice whose corner it is).
- **Corners are selectable, edges are not** (deliberately un-Figma): a corner carries data worth editing
  (position, per-corner color) so it's a selection target; an edge carries none, so it's a **drag-only handle**
  (see `canvas-interaction.md` §Edge dragging) with no selection state — hence no `Edge` in `Part`.
- Each plane is an **ordered list**; element `[0]` is **primary** (what the property panel edits; the anchor
  for range-select).

## Linkage rule (entity ↔ sub-element)

- Selecting an **entity** *populates* the sub-element plane with that entity's editable handles (e.g. pick a
  Slice → its 4 corners become the canvas set), so you can immediately drag corners.
- Selecting **sub-elements** sets their owner as the entity plane's **primary** but does **not** rebuild the
  entity set — so box-selecting corners across two slices leaves both slices as the entity context without
  "selecting" a third unrelated thing.
- Clearing one plane doesn't force-clear the other; switching the shown canvas (different surface/output)
  clears the sub-element plane.

## Interaction verbs (both planes, same semantics)

| Input | Effect |
|---|---|
| click | `Set` (replace with one) |
| shift-click | `Add` / range-extend from primary |
| ctrl/cmd-click | `Toggle` |
| box-drag on canvas | `Add` all handles inside the rect (sub-element plane) |
| esc | `Clear` the active plane |
| drag selected | move set (entity plane → drag-drop targets; sub-element plane → move handles) |

## Mapping to existing code

- `SetupEntitySelection` → the **entity plane**: change `SelectedKind/Id` to an **ordered `List<SelectionTarget>`**;
  add `Slice` + `ContentSource` to `EntityKind`; keep `TryResolve` (drop stale targets against the setup);
  **one shared instance** (scope decision above — windows keep only a pin).
- New **`CanvasSelection`** (shared, like the entity plane — scope decision above) → the sub-element
  plane; same `List<SelectionTarget>`.
- `Interaction/CanvasEditing/CanvasPointHandle` already owns a handle's hit-test + drag; extend it to (a)
  report a `SelectionTarget`, (b) render a selected state, (c) join box-select. `CornerPinHandles` /
  future `AnnotationLineHandles` / lattice handles feed their targets into `CanvasSelection`.
- Property panel reads `primary` of whichever plane is active to decide which editor to show (surface card,
  slice card, corner-color swatch, lattice-point fields).

## Staging (safe to build incrementally, model stays fixed)

1. Entity plane multi-select + `Slice`/`ContentSource` kinds (tree + panel).
2. Sub-element plane single-select (one corner) + property panel wiring.
3. Sub-element multi-select (box-select corners across slices) + group drag.
4. Lattice points as a `SubPart` (once warp lattices exist, §2.3).

The **address form and the two-plane split are the load-bearing decisions** — implement them in step 1 so
steps 2–4 never require reworking selection.
