# Output Setup — Implementation Spec

In-depth spec for the projection-mapping **Output Settings** sidebar and its data model. Goal: surface gaps
and inconsistencies *before* implementation. Written as a set of focused docs rather than one file.

## Documents

| Doc | Covers | Status |
|---|---|---|
| [`output-settings-spec.md`](output-settings-spec.md) | The sidebar UI — annotated mockup + numbered callouts (sections, rows, menus, drag-drop, styling). | drafting (2–3 more passes) |
| [`data-model.md`](data-model.md) | Entities & classes, as-built vs target, gaps/contradictions. All model decisions **settled**. | grounded in code |
| [`implementation-plan.md`](implementation-plan.md) | Dependency-ordered, shippable phases (migration, not greenfield) + first-cut slice. | drafted |
| [`selection.md`](selection.md) | Selection & sub-element editing — two-plane model, address form, staging. | drafted |
| [`states.md`](states.md) | Per-row state matrix (Default/Selected/+Selected/Hovered/Referenced/Dragged/Drop-Target/Unbound/Unused) + tokens. | complete |
| [`canvas-interaction.md`](canvas-interaction.md) | On-canvas editing — contexts, tools, selection, manipulation, snapping, rulers, visual language. | drafted |

`images/` holds the annotated mockups (relative-linked so the spec is portable — the source PNGs were in
Typora's cache, which is fragile).

## Conventions
- **Design source of truth** for the *concept* model: the design-session decisions, mirrored in
  `dev/research/projectionMapping/README.md`. This folder is the *implementation* refinement of that.
- **Undo:** everything except selection/hover is undoable (see the spec header).
- **Grounding:** `data-model.md` cites the actual `Core/Output/` classes; keep it in sync when the code moves.

## Reading order for implementation
1. `implementation-plan.md` — the phases and where to start.
2. `data-model.md` — the entity model (§4 decisions all settled 2026-07-21).
3. `selection.md` — the two-plane selection model (build its address form + split first).
4. `output-settings-spec.md` — the UI, with `states.md` (row states) + `canvas-interaction.md` (canvas behavior).
