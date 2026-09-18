# Output Setup — Plan Folder

Design and implementation docs for the projection-mapping **output setup**: the venue model (surfaces,
outputs, bindings), its editing UI (Board + Flow Outliner), and the long-term scope (calibration,
multi-machine, stage lighting). Written as focused docs rather than one file.

**Terminology is defined once**, in the glossary at the top of
[`ui-restructuring-plan.md`](ui-restructuring-plan.md) — Board, Stage, Flow Outliner, Entity, Card,
Item, Output, Endpoint, Route/Mapping/Binding, Modifier. Older docs predate it; read them through it.

## Active — build from these

| Doc | Covers |
|---|---|
| [`ui-restructuring-plan.md`](ui-restructuring-plan.md) | **Start here.** Glossary + the UI phases: properties → Parameter window (A), Flow Outliner (B), unified Board/Stage canvas + metric grid (C). |
| [`data-model.md`](data-model.md) | Entities & classes, as-built vs target, all model decisions (§4) with their revision history, spaces & units conventions (§5). Grounded in `Core/Output/`. |
| [`refactoring-plan.md`](refactoring-plan.md) | Code-debt review (P0–P4) **and the live progress log** of what has landed on the branch. |
| [`selection.md`](selection.md) | Two-plane selection (entity / sub-element), address form; shared across windows + per-window pinning (2026-08-29). |
| [`shared-selection-slice.md`](shared-selection-slice.md) | **Next implementation slice**: concrete steps for sharing the selection + the entity pin, grounded in the as-built code. |
| [`canvas-interaction.md`](canvas-interaction.md) | On-canvas editing — tools, manipulation, snapping, rulers, visual language. |
| [`states.md`](states.md) | State-token matrix (Default/Selected/Hovered/Referenced/Dragged/Drop-Target/Unbound/Unused). Tokens live; gutter anatomy superseded. |
| [`long-term-features.md`](long-term-features.md) | The rolling backlog / next-major-steps list. Review periodically to prioritize. |

## Reference — consult when the question comes up

| Doc | Covers |
|---|---|
| [`binding-examples.md`](binding-examples.md) | **Output vs. binding, made concrete** — the storage-layering table plus scenarios S1–S7 (device change, venue swap, multi-machine) and virtual displays. |
| [`use-case-flows.md`](use-case-flows.md) | Nine end-to-end click flows (2nd display → touring rig, output packing) + the cross-cutting harvest (readiness panel, Identify, test patterns, the three doors). |

## Long-term scope — not scheduled

| Doc | Covers |
|---|---|
| [`multi-machine.md`](multi-machine.md) | Render clients / boygrouping — machine model, stages, sync primitives, enablers in current work. |
| [`camera-calibration.md`](camera-calibration.md) | Camera-assisted calibration — structured light, reference-point pose, drift check, latency. |
| [`Plan_StageExtension.md`](../Plan_StageExtension.md) | Lighting fixtures, 3D stage models, setup-level reference points; GDTF/MVR + BlenderDMX previz bridge. (In `Plans/` root — broader than output setup.) |

## Historical — do not implement from these

| Doc | Why it's kept |
|---|---|
| [`output-settings-spec.md`](output-settings-spec.md) | The dead sidebar's annotated mockup. Other docs cite its **callout numbers**; callouts 10–15 transfer to the Phase A cards. |
| [`implementation-plan.md`](implementation-plan.md) | Phases 1–3b (how the branch's as-built state came to be) + the "already built" baseline inventory. |
| [`straighten-slice.md`](straighten-slice.md) | Landed thin slice; locked gesture decisions and the Original↔Straight morph rationale. |

`images/` holds the annotated mockups (relative-linked so the docs stay portable — the source PNGs
were in Typora's cache, which is fragile).

## Conventions
- **Concept source of truth:** the design-session decisions mirrored in
  `dev/research/projectionMapping/README.md`. This folder is the *implementation* refinement.
- **Undo:** everything except selection/hover is undoable.
- **Grounding:** `data-model.md` cites real classes — keep it in sync when the code moves.
- **Superseded content is marked, not deleted**, when other docs cite it (callout numbers, decision
  history). New decisions are dated inline so revisions stay auditable.
