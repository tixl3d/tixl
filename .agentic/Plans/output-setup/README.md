# Output Setup — Plan Folder

Design docs and backlog for the projection-mapping **output setup**: the venue model (surfaces, outputs,
bindings), its editing UI (Board + Flow Outliner), and the long-term scope (calibration, multi-machine, stage
lighting).

**Status (2026-10-31):** the UI phases, the refactoring programme and Player export have shipped. There is no
execution plan in progress; open work lives in the backlog. The code is the source of truth: the design docs
predate parts of it, so check `Core/Output/` and `Editor/Gui/Windows/OutputSetup/` before relying on them.

## Backlog

[`long-term-features.md`](long-term-features.md) is the single list of open work. Review it periodically to
prioritize.

## Design References

| Doc | Covers |
|---|---|
| [`data-model.md`](data-model.md) | Entities and classes, model decisions (§4), spaces and units (§5). |
| [`selection.md`](selection.md) | Two-plane selection (entity / sub-element) and per-window pinning. |
| [`canvas-interaction.md`](canvas-interaction.md) | On-canvas editing: tools, manipulation, snapping, rulers. |
| [`states.md`](states.md) | State-token matrix (Selected, Hovered, Referenced, Unbound, Unused, ...). |
| [`binding-examples.md`](binding-examples.md) | Output vs. binding: storage layering and scenarios S1-S7. |
| [`use-case-flows.md`](use-case-flows.md) | Nine end-to-end click flows and the cross-cutting harvest. |

## Long-Term Scope (Not Scheduled)

| Doc | Covers |
|---|---|
| [`multi-machine.md`](multi-machine.md) | Render clients and boygrouping. |
| [`camera-calibration.md`](camera-calibration.md) | Camera-assisted calibration, drift check, latency. |
| [`Plan_StageExtension.md`](../Plan_StageExtension.md) | Lighting fixtures, 3D stage models, GDTF/MVR. |

## History

[`output-settings-spec.md`](output-settings-spec.md) is the removed sidebar's annotated mockup, kept because other
docs cite its callout numbers. Finished execution plans are in
[`../archive/output-setup/`](../archive/output-setup/), including the refactoring progress log.

`images/` holds the annotated mockups.

## Conventions

- **Undo:** everything except selection and hover is undoable.
- **Grounding:** `data-model.md` cites real classes; keep it in sync when the code moves.
- **Superseded content is marked, not deleted**, when other docs cite it. Date new decisions inline.

## Glossary (canonical terms — 2026-07-29)

Specs, code, and discussions use these; aliases listed are **deprecated**.

**Views**
- **Board** — the 2D unfolded overview canvas; the home view. *(replaces: OutputCanvas, "unified canvas", "overview")*
- **Stage** — the 3D venue view.
- **Camera presets** — Output (through a projector's frustum), Straight (ortho facing a surface),
  Content (ortho onto a source). Cameras entered via entities, not modes (open question 5).
- **Flow Outliner** — the bottom panel: columns + pills + connections. *(avoid: "strip", "sidebar")*

**Entities** (generic term: **Entity** — matches `SetupEntitySelection.EntityKind`; an *Item* is an
entity's outliner representation, never the model object — see Representations)
- **Content** (source), **Slice**, **Surface**, **Region** (child surface — display name, settled),
  **Output**, **ReferenceImage**, **Prop**, **Machine**; future: Camera, Fixture, StageModel,
  ReferencePoint.
- **Output** — a logical pixel canvas; "the part that must survive the cable being pulled".
- **Endpoint** — anything an output can be bound to on a machine: a Display (physical or virtual),
  a Spout/NDI sender, later an Art-Net node. *(replaces: "Device" — too hardware-flavored for
  Spout/NDI; "Endpoint" matches the WASAPI vocabulary the audio abstraction already uses)*
- **Virtual display** — an always-existing display endpoint (`2nd Display`, `Editor Display`).
- **Patch** (2026-08-31) — an output-side canvas region on the direct pipe:
  `{ SliceId, Quad, Name }`; an axis-aligned rect = packing tile, a warped quad = surface-less
  keystone (data-model §2.5). *(leaning "Patch"; "Tile" recorded as the alternative — never "slice",
  one name per end of the pipe)*

**Relations** (all drawn as **Connections** — the view-level word for any line)
- **Route** — slice → surface/output content assignment.
- **Mapping** — the surface → output edge; carries quad + modifiers.
- **Binding** — the output → endpoint assignment, per machine. **Reserved for exactly this** — a
  Binding is *one kind* of connection, never the generic word for a line (collision resolved in
  favor of `DeviceBinding`/`binding-examples.md` usage).
- **Derivation wire** (2026-08-31) — a **dashed** connection for authoring dependencies (reference
  jig → derived slices, data-model §2.10); solid wires are always the render path.

**Representations & modifiers**
- **Card** — an entity's canvas representation on Board/Stage (ContentCard, SurfaceCard,
  OutputCard, ReferenceCard, PropCard).
- **Item** — an entity's Flow-Outliner representation (drawn by `OutlinerItem`; nests naturally once
  hierarchy becomes containment). *(replaces: "pill", "row" — both bake in a flat shape; confirmed
  2026-09-05: manual tests and UI text say **item**, e.g. "the Surface 1 item in the SURFACES column")*
- **Modifier** — a verb-attached refinement on a mapping or output: Mask, Warp, ColorCorrection.
  *(replaces: "SubEntity", "Filter" — SubEntity collides with the selection model's* sub-element*)*
- **Sub-element** — a canvas handle (corner, lattice point, annotation endpoint); the second
  selection plane (`selection.md`). Not an entity.
