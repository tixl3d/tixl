# Output Setup: Architecture Review Plan

A plan for one thorough review of the output setup before it ships. The deliverable is a ranked findings
document. Fix nothing during the review.

## Why this review

The surface-to-patch connection looked like a stored link, but it was recomputed by comparing quads. An unrelated
edit (lengthening a wall) made connections vanish. Each symptom could be patched on its own, and the first patch only
hid the problem. Only following the behaviour back to the data model showed what was wrong.

**This review looks for that class of problem:** where the model says one thing, the code assumes another, and the
gap only shows when two features meet. Style and small cleanups are out of scope unless they point at such a gap.

## Before starting

1. Read `.agentic/AGENT_INSTRUCTIONS.md`, especially "Relationships are stored, not inferred".
2. Read `.agentic/Reviews/FEATURE_REVIEW.md`. This plan narrows its scope and goes deeper; use its findings format.
3. Read `data-model.md`, `selection.md` and `states.md` in this folder. They may be out of date, and a mismatch between
   them and the code is itself a finding.
4. Rules: no commits, no `gh`, no fixes. Runtime experiments only in the `_agentTests` project (see
   `.agentic/DEBUG_PROTOCOL.md`). The user launches the editor.

## Scope (about 28,000 lines)

| Area | Files |
|---|---|
| Model | `Core/Output/`: `Setup`, `Surface`, `OutputDefinition`, `Slice`, `FloorPlan`, `MachineConfig`, `SetupRepair`, `SurfaceGeometry` |
| Rendering | `Core/Output/Rendering/`: `OutputCompositor`, `OutputContentResolver`, `OutputStreaming` |
| Editing logic | `Editor/Gui/Windows/OutputSetup/`: `SetupActions`, `SetupRouting`, `SetupRelations`, `FloorPlanSync`, `ContentSourceSync`, `WallpaperLayout`, `RegionSlices`, `SurfaceMetrics` |
| Views | `SetupOutputView.*` (the Board alone is 2,400 lines), `SetupFlowOutliner`, `SetupParameterView`, `OutputSetupModeView`, `CalibrationOverlay` |
| State | `Editor/UiModel/ProjectHandling/OutputSetupHandling`, `Editor/UiModel/Selection/*` |
| Operators | `Operators/Lib/Symbols/render/output/`: `SendToOutput`, `StageGeometry`, `ProjectOntoStage` |
| Player | How the Player loads and runs a setup, including `SetupFiles` |

Read the model and the editing logic completely, and read the views for how they use the model.

## Phase 1: Map the model

Write this down first, because the later phases check against it.

For every persisted entity, list its fields and classify each one:

- **Identity or stored link:** an id pointing at something. What keeps it valid when the target is deleted, duplicated
  or reloaded, or when the undo stack restores it?
- **Source of truth:** a value only this field holds.
- **Derived but stored:** a copy of something computable. Who keeps it in step, and what happens when they disagree?
- **Machine-specific:** does it leak into the shared setup file, or does shared data leak into the machine file?

Then draw the real relationship graph, marking which edges are stored and which are computed. Compare it with
`data-model.md`.

## Phase 2: The architecture questions

These are the main work. Give each one an answer with `file:line` evidence, including "checked, this is fine".

### A. Inferred relationships

Search for anything that decides identity or connection by comparing values.

Known candidates to judge:

- Finding a patch by matching a surface's name: `StageGeometry.TryFindPatchQuadByName`, `SetupRelations` (~line 395),
  `FloorPlanSync.IsInUse` (~line 583). Name matching is documented for the pixel-map workflow. Renaming a wall silently
  changes where its picture lands, so is that acceptable, and can a wall and a patch be renamed apart without warning?
- The **implicit patch** and **implicit slice** (`TryGetImplicitPatch`, `SetupLabels`): "first, unnamed, covering the
  full canvas". Is an entity's role derived from its current values? Turning, naming or moving it silently changes what
  it is and how it is listed. Should this be stored?
- `SetupRepair.LinkPinsSavedWithoutPatchIds`: runs on every load rather than once. Can it re-link a pin the user
  deliberately unlinked? Is a format version available to run it only once?
- `Surface.PointAims`, `QuadBeforeAligning`, `Plugs.BoundPlugId`, `PlugBinding.DisplayName`/`DisplayIndex`: what
  identifies a display across reboots and on another machine?
- `ContentSource.SymbolChildId`: the source is matched to an op by child id. What happens with several instances of a
  symbol, nested compositions, or a copied op (same symbol, new id)? `TryFindSendParent` scans every symbol in every
  package and takes the first.
- `ContentSource.IsRenamed` and the name sync with the op: who owns the name?
- Slice names, which **Update Slices from Regions** now overwrites: is a slice linked to its region, or only by
  `region.SliceId`? It decides by "the slice this region shows", so what if a user re-routes one by hand first?

Search terms: `Tolerance`, `string.Equals`, `.Name ==`, `Distance`, `FirstOrDefault`, `[0]`, `Patches[0]`,
`OutputMappings[0]`, `Find*By*`.

### B. Two owners of one fact

Find facts with more than one writer or more than one representation.

- A surface's size and position: `SizeInMeters`, `Anchor`, `Placement.Pose`, `BoardPlacement`, floor plan segments.
  `FloorPlanSync.Apply` and `FollowWallWidth` run in opposite directions. Is there a stable direction? Can they fight?
- Pin quad, `PatchId` and `Mode`: are all states valid? `SyncPinsToLinkedPatches` runs per frame, so which edits does it
  silently overwrite? Check every writer of `mapping.Quad`, especially any that doesn't call `PromoteToCornerPin`
  (`SetupOutputView.cs:~909`, `Measure.cs:~349`, `PinCalibration.cs:~192` and `~307`, `SetupParameterView.cs:~408`).
- Output resolution: `CanvasResolution`, `ResolvedResolution`, `CanvasSize`, the send's Resolution input, the
  requested resolution from the context.
- Routing that exists in two places: `surface.SliceId`, `patch.SliceId`, `mapping`, `SendToOutput.TargetIds`. Are the
  op's target list and the setup's routing the same fact?
- Selection: the entity selection, node selection, `_shownSurfaceId`, `_canvasSelection`, `GlobalSelectionHandling`.
- The output name, now derived through `SetupLabels.OutputLabel`: which readers still use `output.Name` (for example
  `SetupRepair`, logs, and code in Core or the Player)?

### C. Meaning that changes with context

- The same field meaning different things by state. For example, `LocalPosition` relative to the parent, `Anchor`, and
  coordinate spaces: canvas 0..1 Y down, metres Y up, photo pixels. Trace each conversion. Is any space implicit?
- The `Canvas` input on `StageGeometry` takes an output, a surface or nothing, and the picker also offers reference
  images. Is one `Guid` with three meanings right?
- `Prop` holds `Kind = Person` while the UI says Person everywhere and the code says Prop. Is that a real concept?
- `ContentSource` kind keys: the entity id is the op's child id, not the source's id. Are the two ever confused?

### D. Lifecycle: delete, duplicate, undo, reload

Build a table with one row per entity and one column per operation. Mark what happens to everything pointing at it.

- Operations: delete, duplicate, undo of each, redo, setup switch, setup rename, op deleted in the graph, op hot-reloaded,
  project reopen, output removed, display unplugged, opening on another machine.
- Look for dangling ids, copies still pointing at the original, and children left behind. For duplication, check
  `CloneSurface`, `DuplicateChildrenOf`, duplicating a whole setup ("ids preserved"), and region pins.
- `RegionSlices` and `WallpaperLayout`: their undo pairs a setup snapshot with graph commands. Is the order always safe?
- Machine bindings live outside the undo stack. What does undo do when an output and its binding diverge?

### E. Undo and persistence

- Is every mutation inside an undo step, and is every step saved? Search for writes outside `SetupUndo`, gestures,
  and commands.
- Whole-setup JSON snapshots: cost on large setups, and whether anything holds an object reference across a snapshot
  restore (`Annotation` references in `SurfaceRectSnapshot`, cached `Surface` fields, `_gesture.Snapshot`).
- Derived values at save time: `SaveActive` syncs pins before writing, but do undo snapshots capture the synced values?
- Static state shared across windows or projects: `OutlinerItem`, `CalibrationOverlay`, `SetupOutputView.PendingPlanDrawId`,
  `PlanMenuPointInPlan`, `SetupActions._deleteBuffer`. What happens with two output windows or two open projects?

### F. Mutating during iteration and frame order

The Alt+drag crash came from this: a list was changed while drawing walked it.

- Find every mutation of `setup.Surfaces`, `Slices`, `Outputs`, `Patches`, `ContentSources` reachable from draw code.
  Check for `foreach` callers higher up the stack.
- List the frame order: `UpdateFrame`, sync, drawing, picking, gestures, rendering. What reads a value before this
  frame's edit, and does anything render one frame stale in a way a user would see?
- Deferred-request statics that act on a later frame: can the target be gone by then?

### G. Editor and Player parity

- Does the Player render the same picture from the same file? Trace `OutputCompositor` and the resolver for anything that
  depends on editor-only code, such as `SyncPinsToLinkedPatches`, which only runs in the editor and at save.
- Do `Core` types depend on editor concepts, or does the editor reimplement something the Player needs?
- The planned cross-platform port: list new Windows-only touchpoints, for example `explorer.exe` in `RevealActiveFile`.

### H. Structure and size

Only where it points at a real risk: `SetupOutputView.Board.cs` and `SetupParameterView.cs` by size, and whether editing
logic lives inside draw methods. Is there a clean boundary that would let this logic be tested without a UI?

## Phase 3: Scenarios that cross features

Walk these through the code, and through the editor if the user is available. Each one combines features that were
built separately, which is where the last problem hid.

1. Trace a surface, add regions, create slices, map regions to patches, then edit the floor plan, re-trace, and update.
2. Duplicate a surface with regions, slices and patch links, then undo twice and redo once.
3. Delete a patch, an output, a region and a `SendToOutput` while each is linked, then undo each.
4. Rename everything that can be renamed, then check what `StageGeometry` and the outliner still find.
5. Copy a `SendToOutput` in the graph, nest it in a new symbol (**Combine**), and open the composition inside it.
6. Save on one machine and open on another with different displays and no NDI package.
7. Switch setups with a gesture running, a menu open, or a rename field active. Switch projects with two output windows.
8. Export a Player build of a setup using regions with pins, patch links, a floor plan and `ProjectOntoStage`.
9. Make a pin non-finite or collapse a region to minimum size, then save and reload.
10. Start with an empty setup and follow `use-case-flows.md` for each listed use. Does each flow still work as written?

## Phase 4: Report

Write to `.agentic/Reviews/OutputSetup_ArchitectureReview.md`.

For each finding:

- **Severity:** Data loss or wrong output first, then silent surprise, then fragility, then cost.
- **What the code assumes vs. what is true**, with `file:line`.
- **A concrete failure:** the steps a user would take to see it. If you can't describe one, mark it as a suspicion.
- **Root cause in the model,** not just the symptom. Group symptoms that share a cause under one finding.
- **Proposed change,** its size, and any format or migration consequence. Note what must happen before release, since
  file format changes are cheap only until then.

Also include:

- The Phase 1 model map, as a reusable reference.
- The Phase 2 lifecycle table.
- Questions answered "this is fine", so they aren't checked again.
- Decisions that need the user, stated as options with a recommendation.

## Using parallel reviewers

The review splits cleanly. If running with several agents, do Phase 1 first and give all of them its map, then assign
Phase 2 areas A-B, C-E and F-H separately. Each should verify its findings against the code before reporting, because
a plausible finding in code this interconnected is often already handled elsewhere. One agent merges the results and
removes duplicates by root cause.
