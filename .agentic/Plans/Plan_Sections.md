# Sections (annotation rework)

Implements GitHub issue **#1037 "Improve annotation interaction"**: rename Annotations to **Sections**, give them an explicit membership model (`sectionId` on graph items), and improve resize/expand/visibility UX for large graphs.

Consumers of the new section tree beyond the graph itself:
- [`Plan_SnapshotControlView.md`](Plan_SnapshotControlView.md) Phase C — grouping controlled ops by section.
- Timeline dope-sheet grouping/sorting of animated parameters (upcoming, per issue).
- Potential graph navigation (breadcrumbs / center-on-section).

## Goal

Sections become a first-class structural element: membership is an explicit, serialized, undoable property — not a per-frame geometric test. On top of that model: better resizing, automatic overlap avoidance, slow-resize ("push the border") interactions, and viewport-clamped titles.

## Architectural decisions (locked in)

- **Rename "Annotation" → "Section"** in UI, code, and serialization. Reading old `.t3ui` files gracefully maps `Annotation` → `Section`. Hotkey: introduce the new binding; keep the old one as a hidden alias for at least one release (muscle memory).
- **Ownership is derived state.** (Revised 2026-07-04, supersedes "explicit undoable membership".) Ops carry a serialized `SectionId`, sections a `ParentSectionId` — but both are a *cache of geometry*, re-derived by `SectionTree.UpdateOwnershipFromGeometry` on load and on every structural layout refresh (moves, resizes, paste, delete, undo/redo). No membership commands on the undo stack: undoing a move restores positions, and derivation restores ownership implicitly. Commands stay reserved for actual user gestures.
  - Assignment rule: an op belongs to the **innermost** section fully containing its rect; a section nests into the innermost *strictly larger* one containing it. Overlapping non-nested sections: innermost-by-area wins.
  - **Collapsed sections neither adopt nor release.** Only their header renders, so geometry against the invisible stored rect would silently hide loose ops dropped there. Membership in a collapsed section is kept while the op stays inside the stored rect.
  - Legacy-graph edits reconcile automatically on the next MagGraph layout refresh — no manual cleanup action needed.
- **Collapse state is stored on the section** (today: `Child.CollapsedIntoAnnotationFrameId` per op). The per-op flag is refactored into a fast runtime lookup `GraphItem.IsHiddenInCollapsedSection` (not serialized) for MagGraph layout and connection routing.
- **Member order lives on the section** (serialized list of child ids), defaulting to canvas position order when absent. Wanted identically by the dope sheet and the snapshot control view; introduced when the first consumer needs manual ordering — derived order until then.
- **One shared tree helper.** A single builder (e.g. `SectionTree.Build(SymbolUi)`) producing sections → nesting → member items in display order. Graph, dope sheet, and snapshot control view all consume this; none re-derives structure.
- **Overlap resolution must terminate.** Resolve pushes in the direction of the triggering change, single pass per axis with a capped iteration count — no unbounded recursion (cyclic overlap arrangements can ping-pong a naive solver).
- **Legacy Graph**: keeps working read-only with sections (rendering, membership respected); auto-expand and slow-resize are MagGraph-only.
- Existing section coloring / title styling remains.

## Current state — what exists

- [`Editor/UiModel/Annotation.cs`](../../Editor/UiModel/Annotation.cs) — `Label`, `Title`, `Color`, `PosOnCanvas`, `Size`, `Collapsed`; no membership, no parent reference, no order.
- [`Editor/UiModel/SymbolUi.Child.cs`](../../Editor/UiModel/SymbolUi.Child.cs) — `CollapsedIntoAnnotationFrameId` (per-op hidden-in-collapsed-frame marker; to be replaced per decisions above).
- [`Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawAnnotation.cs`](../../Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawAnnotation.cs) — drawing, header hit-testing, collapse, current geometric containment logic.
- [`Editor/UiModel/SymbolUiJson.cs`](../../Editor/UiModel/SymbolUiJson.cs) — serialization of annotations and child flags; back-compat reader location.
- [`Editor/Gui/MagGraph/Interaction/GraphContextMenu.cs`](../../Editor/Gui/MagGraph/Interaction/GraphContextMenu.cs) — current "Add annotation" / hide entries (to surface in a main menu per issue point 3).
- MagGraph layout/routing: `Editor/Gui/MagGraph/Model/MagGraphLayout*.cs` — where the resolved section reference speeds up layout (issue implementation detail 2).
- Undo: `MacroCommand` infrastructure under `Editor/UiModel/Commands/`.

## Phases

### Phase 1 — Model + rename (foundation; unblocks dependent plans) — DONE 2026-07-05

**Goal:** sections exist as a tree with explicit membership; everything renamed; old files load.

**Scope:**

1. Rename `Annotation` → `Section` (class, fields, UI strings, serialization key with back-compat reader mapping `"Annotations"` → sections on load; write new key only).
2. Add `SectionId` to `SymbolUi.Child` (serialized, optional) and `ParentSectionId` to `Section` (serialized, optional). Resolve both into fast references in MagGraph layout.
3. Ownership derivation wired into load and the structural layout refresh (covers move/resize/paste/delete/undo/redo and legacy-graph edits); interactions just flag the structure as changed. No migration step needed — derivation applies to old files the same way.
4. `SectionTree.Build(SymbolUi)` helper + collapse state moved onto the section; `IsHiddenInCollapsedSection` runtime flag replaces per-op serialized field (reader still accepts the old field).
5. Hotkey + menu: rename to "Add Section", keep old shortcut as alias; expose add/hide in a Graph/Edit main menu (issue point 3).

**Verification:** old projects load with identical visual result; collapse/expand round-trips; undo restores membership with positions; manual test set added.

### Phase 2 — Resize & interaction polish — DONE 2026-07-05

**Scope (issue points 2, 3, 6 partial):**

1. Resizing on all corners and edges (today: limited).
2. Collapse toggle sized/scaled with title font (`T3Ui.UiScaleFactor`-correct).
3. Header hit-test fix when zoomed in (accidental clicks on invisible header near edges — issue point 6 status quo).

**Implementation notes:** header interaction disables once a frame covers ≥70% of the viewport; no top-left corner resize handle (collapse chevron sits there — left/top edges cover it); small frames fall back to bottom-right-corner-only resizing; collapsed frames aren't resizable.

### Phase 3 — Auto-expand & overlap avoidance — DONE 2026-07-05

**Scope (issue points 4, 5):**

1. Layout solver: expanding a section pushes neighboring sections (and their contents) to avoid overlap, preserving relative positions; capped-iteration, direction-of-change resolution.
2. Invoked on: insert into stacks, section expand, add/duplicate/paste near bottom/right borders, (evaluate: collapse). All resulting moves folded into the triggering MacroCommand.

**Implementation notes:** solver lives in `SectionTree.CollectOverlapPushes` (scope-aware: pushes siblings within the same parent) with `ResolveBoundsExpansion` cascading parent growth up the ancestor chain. Programmatic op displacement funnels through `MagItemMovement.MoveItems`, which grows the owning frames — covering splice inserts, added multi-input rows, and placeholder insertion uniformly. Expand is undoable (`ChangeSectionCollapseCommand`); collapsing does not pull neighbors in. Collapsed frames occupy only their header strip for pushing/nesting; ownership of hidden ops/frames is fully sticky.
3. Slow-resize mode: dragging nodes slowly against a section border grows the section; dragging a border slowly pushes neighbors. Disabled while the section is selected or Shift is held. Unlock speed smoothed, tunable via `UserSettings.SectionSlowResizeSpeed` (screen px/s, default 20, 0 disables). Bottom/right borders only. Border-push previews statelessly from frozen neighbor positions, so neighbors follow a retreating border during the drag.

### Phase 4 — Title visibility (clamped/stacked titles)

**Scope (issue point 6):**

1. Clamp section frame border + title position to the viewport when the section is cut off top/left, so titles stay readable.
2. Double-click on a clamped title still renames.
3. Multiple nested sections cut off → keep innermost title; optional stacked-titles treatment with timed cross-fade (stretch goal).
4. Decide whether clamped borders render sharp (no rounding) on the clamped side.

### Phase 5 — Extras (optional)

- Section background image attribute (issue point 7).
- Graph toolbar "Add Section" button.
- Persisted member order + drag-reorder (when the dope sheet or snapshot control view asks for it).

## Open questions

- Should `SectionId` apply to inputs/outputs of the composition too (issue implementation detail 1 says "all UI elements")? Concrete consumer unclear — defer until one exists. Until then inputs/outputs travel with frames by geometric containment.
- Dropping a section whose bounds don't fit the target frame (e.g. a collapsed bar wider than the frame): auto-grow the target on drop? Deferred — geometry currently refuses the nesting.

Resolved: section drags move explicit members and re-parent by geometry at the drop location; nested collapse state is preserved through outer collapse/expand round-trips.

## Documentation

- Update `.help/` graph-organization page (rename + new interactions) per phase; manual test sets per phase in `.tests-manual/`.
