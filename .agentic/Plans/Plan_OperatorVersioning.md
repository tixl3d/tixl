# Plan: Operator Versioning and Migration

## Motivation

Breaking changes to operators currently go like this: rename the old op by hand (`_TimeOld`), tag it
Obsolete, and create a new op under the good name. The underscore pushes the old op down in search
(`SymbolFilter` ×0.1). Problems:

- No link between versions, and no order once there are three versions.
- No migration help: users find old ops by chance and rebuild them by hand.
- No parameter conversion. Values, connections, animations and snapshots are lost when swapping.
- Six or more naming styles in Lib: `_TimeOld`, `_Time_old`, `_GridPoints_Old`,
  `_KeepPreviousFrame_Old1`, `__ObsoletePulse`, `_LegacyAudioReaction`. Trailing digits already
  mean either arity (`AddVec2`, `Atan2`) or "second version" (`AfterGlow2`).
- About 38 old ops in `Operators/Lib`, and 51 `.t3ui` files with the Obsolete tag.

Goal: the developer records a new version in one action. The editor shows where outdated ops are
used, upgrades them with values and references intact (undoably), and old ops can eventually be
deleted without breaking projects.

## Decisions Already Made

1. **Superseded ops are renamed once to `{Name}_v{N}`, and that name never changes again.** The
   newest version always keeps the plain name. `N` counts generations: when v2 arrives, `Time`
   becomes `Time_v1`; when v3 arrives, `Time` becomes `Time_v2`. The `_v` separator avoids clashes
   with `Vec2`/`Atan2`. No leading underscore.
2. **The version order and the release live in data, not the name.** The new version's `.t3ui`
   holds a `Supersedes` record (below). The UI derives labels such as "Time · v1 · superseded in 4.5".
3. **The record is stored on the successor.** The old op's files are never needed for migration.
   That is what makes it safe to delete old ops later.
4. **The upgrade creates a new child id.** Instead of keeping the id, as many references as possible
   are moved from the old child to the new one (list in Phase 2).
5. **The data is recorded when the version is created** ("Create new version" action), not guessed
   later. Guessing by name and type happens only once, for the existing backlog, and a person
   reviews the result (Phase 5).
6. **The new version keeps the old op's input and output Guids.** Nothing in TiXL requires slot ids
   to be unique across symbols: all lookups go through symbol or child, and Lib already shares 119
   slot ids between different ops. So "same id = same parameter", and the record only needs to list
   exceptions.

## Data Model

New optional top-level field `Supersedes` in the successor's `.t3ui`. The field is additive, so no
`SymbolFormatVersion` bump is needed:

```json
"Supersedes": [
  {
    "Id": "<symbol id of Time_v1>",
    "Name": "Lib.numbers.anim.Time_v1",
    "Since": "4.5.0",
    "Inputs":  { "<old input id>": "<new input id>", "<dropped input id>": null },
    "Outputs": { "<old output id>": "<new output id>" },
    "Changes": "Speed is now in bars, not seconds. Divide old values by the BPM."
  }
]
```

How an old slot is resolved, in this order:
1. An explicit entry in `Inputs`/`Outputs`. `null` means "dropped on purpose", so no warning.
2. The same id exists on the new op with the same value type, so it is carried over.
3. Otherwise the slot is **unmapped**. If the old child had a non-default value, connection or
   animation on it, the upgrade reports it as "check manually" and shows `Changes`.

`Changes` is a required Markdown text describing what changed in this version. It is written in
the "Create new version" dialog and serves as the version history (Phase 3) and as the migration
hint in upgrade reports.

**Runtime migration never matches by name.** Name matching is an authoring-time tool only.

`OperatorVersionIndex` is built after load and rebuilt when `SymbolStructureVersionCounter` changes.
It maps old symbol id → (current successor id, composed slot map). Chains such as v1 → v2 → current
are composed once, so upgrading a v1 op jumps straight to the newest version. Successors in other
packages work too, because all lookups are by id. "Outdated" means: listed in some `Supersedes`,
**or** tagged `Obsolete` (an op removed without a successor).

## Research Findings (2026-11-04)

**Existing code to build on**
- `Editor/UiModel/Modification/ChangeSymbol.cs` already implements "Replace with…". It is undoable,
  uses a `MacroCommand`, and matches by name and type. Only the legacy graph menu
  (`GraphView.cs:806`) can reach it.
  - It doesn't carry over: animations, variations/snapshots, bypass, output IsDisabled /
    DirtyFlagTrigger / TimeClip data, comment, style, size, SectionId, snapshot flags,
    ConnectionStyleOverrides, tour points, output-setup sources.
  - It skips inputs with more than one connection (`:79-83`).
- `CopySymbolChildrenCommand` is the only fully undoable copy-with-remap path, but it assumes the
  same symbol type.
- Animations: `Animator._curvesByChildAndInput[childId][inputId]`. `AddCurvesToInput` works by id,
  but there is no undoable id-based curve-move command. Call
  `Symbol.CreateOrUpdateActionsForAnimatedChildren()` after adding curves.
- Variations: `Variation.ParameterSetsForChildIds[childId][inputId]`. Unknown ids are silently
  dropped on read. There is no undoable re-key command.
- **Output setup:** `ContentSourceSync.DropDeletedSources` removes a source *and its slices* as soon
  as its child id is gone, and this can't be undone. `ContentSource.SymbolChildId` must be remapped
  **before** the old child is deleted (undo pattern: `SetupSnapshotCommand`).
- Tour points (`ChildId` + `InputId`) and `OutputWindowState.PinnedInstancePath` / `PickedCameraId`
  have no remap.
- Bookmarks, session paths and `ViewedCanvasAreaForSymbolChildId` all tolerate stale ids. Best
  effort only.
- **Side effect:** `Random` and `FloatHash` seed from `SymbolChildId`, so the upgraded op produces
  different random values.
- Rename: `SymbolNaming.RenameSymbol` recompiles the package, can't be undone, is blocked for
  read-only packages, and moves `.cs/.t3/.t3ui` via `SymbolPathHandler`.
- Duplicate: `DuplicateAsNewType` gives every slot a new Guid (`MemberDuplicateRewriter`,
  `Duplicate.cs:524-585`), recompiles, and calls `UndoRedoStack.Clear()`.
- Lib can be edited only in DEBUG builds (`ProjectSetup.Startup.cs:44-65`). In Release it is
  read-only, but its `.t3ui` is still loaded, so `Supersedes` records in Lib reach every user.
- `.t3ui`: optional fields follow the `Tags` pattern in `SymbolUiJson.WriteSymbolUi` / `TryReadSymbolUi`.
- **Existing bugs:**
  - `SymbolUi.ReplaceWith` (`SymbolUi.cs:343`, used by hot reload) copies description and links but
    **not Tags**.
  - `SymbolUi.CloneForNewSymbol` doesn't copy Tags either.
- Missing (deleted) ops: `UnresolvedChild.Json` keeps the whole raw child, but it is `internal` to
  Core. Unresolved animations are private.
- Slot-id hazard: `Symbol.Child.UpdateIOAndConnections` (`Symbol.Child.cs:417-436`) drops any
  connection if *either* slot id matches a removed definition. With shared slot ids, removing an
  input on the new op could drop a connection between unrelated ops.
- Cross-op class references can break a rename: `AudioReaction.InputModes` (PlaybackFFT) and
  `MouseInput.SelectedChildId` (VisibleGizmos, _SketchImpl). `RecompileSymbol` reverts the source if
  compilation fails.
- Editor code that refers to op symbol ids keeps pointing at the *old* op after "Create new version":
  `OpUi` custom UIs, `AssetHandling`, `TemplateDefinitions`, `CommandOutputUi`,
  `PlaceholderCreation`, `ConnectionMaker`.
- UI hooks:
  - Node status badge template: `MagGraphCanvas.DrawNode.cs:1118-1155`.
  - Parameter header icon block: `ParameterWindow.cs:226-269`.
  - Search loop (exclusion would be a `continue`): `SymbolFilter.UpdateMatchingSymbols` `:104-160`.
  - Welcome notice: `WelcomeWindow.DrawPreUpgradeBackupNotice` `:113`.
  - Dialog template: `MissingOperatorsDialog`.
  - Icons: `Flame`, `ArrowUp`, `Warning`.

## Phases

### Phase 0: Model, index, fixes
- New namespace `Editor/UiModel/Versioning/`:
  - `SupersededSymbol` record.
  - `OperatorVersionIndex` (build, compose chains, `TryGetSuccessor(symbolId, out …)`,
    `IsOutdated(symbolUi)`, `GetVersionNumber`, `GetHistory`).
  - Pure `SlotMapping` resolver (explicit → same id and type → unmapped).
- Serialize `Supersedes` in `SymbolUiJson`.
- Fix `SymbolUi.ReplaceWith` to also copy Tags and Supersedes.
- Fix the slot-id hazard in `Symbol.Child.UpdateIOAndConnections`: compare removed ids per side
  (source slot on this child, target slot on this child). This is a small Core bug fix.
- Unit tests in Core.Tests for `SlotMapping` and chain composition. Pull the pure file in with
  `<Compile Include="../Editor/…">`, the way `ImGuiLayoutScaling.cs` is included.

### Phase 1: "Create new version" action
Context menu "Symbol Definition > Create New Version…", enabled for editable packages only.
1. Pre-check: grep the package sources for references to the class name. If any are found, list
   them and stop.
2. Rename `Time` → `Time_v{N}`, where `N` = 1 + the number of predecessors already in the chain
   (`SymbolNaming.RenameSymbol`).
3. Duplicate it as `Time` in the same namespace and package. Add a `keepSlotIds` option to
   `MemberDuplicateRewriter`. Only the symbol id is new.
4. On the new op:
   - Copy Description, Tags (e.g. Essential), Links and InputUi settings.
   - Write the `Supersedes` record: the predecessor's own records plus a record for `Time_v{N}`
     with empty maps, `Since` = the current `TixlVersion`, and the `Changes` text from the dialog.
     The dialog has a required "What changed in this version" field, which can be refined later.
5. Show the hint: "Now change Time. If you delete or replace an input, map it in the Supersedes
   editor (Phase 3), or leave it unmapped."

Editor code that refers to the old symbol id (custom `OpUi`s, `TemplateDefinitions`,
`AssetHandling`, `PlaceholderCreation`, …) keeps pointing at the old version. This is **not**
fixed automatically. When the version index is built, a check over the known id registries
(`OpUi` draw functions, template definitions) logs a warning for each registration whose symbol id
has been superseded. The developer or an agent updates them in a separate commit.

This means two package rebuilds. It can't be undone, the same as Duplicate today. Both are
acceptable for a rare action meant for developers. Merging the two into one rebuild is optional
later.

### Phase 2: Upgrade command (live, undoable)
`OperatorUpgrade.TryCreate(compositionUi, childIds, out MacroCommand, out UpgradeReport)`, based on
`ChangeSymbol.cs` and replacing its name matching with `SlotMapping`. Steps, in order:
1. `AddSymbolChildCommand` (new id) with the same position, size and name. Copy the ChildUi extras
   (comment, style, SectionId, snapshot group, `SnapshotEnabledInputIds` and
   `ConnectionStyleOverrides`, both remapped) with a small new undoable `CopyChildUiCommand`.
2. Non-default input values (`ChangeInputValueCommand`), bypass, and output IsDisabled /
   DirtyFlagTrigger / TimeClip data.
3. Input connections, including multi-input fan-in in order (removes the ChangeSymbol limit).
   Output connections keep their multi-input index, rewired in reverse order as `Combine` does.
4. A new `MoveAnimationsCommand` (old child + input → new child + input, by id; undo moves them
   back).
5. A new `RekeyVariationsCommand` (child id and input ids in the composition's variation pool,
   then save).
6. Output setup: remap `ContentSource.SymbolChildId` with `SetupSnapshotCommand`. This must run
   before the delete.
7. Tour points and `OutputWindowState` pin/camera paths: remap.
8. `DeleteSymbolChildrenCommand` on the old child, then update the selection.

`UpgradeReport` lists each unmapped slot and why (value / connection / animation), the record's
`Changes` text, and a fixed warning: "Random seeds change because they depend on the operator's
id." This was accepted on 2026-11-04.

Entry points:
- Parameter window: an Upgrade icon button in the header (`iconCount`+1), with a tooltip that shows
  the report preview.
- MagGraph context menu "Upgrade to [Time]", which works on all selected outdated ops.

Integration test through the debug bridge: build an old op with a value, connection, animation and
snapshot; upgrade; check `getGraphState`; undo; check again.

### Phase 3: Show outdated ops
- MagGraph node badge: `Icon.Flame` in `StatusAttention`, using the status-badge template. Hide it
  below a canvas-zoom threshold. Clicking it runs the upgrade, and the tooltip shows "Superseded by
  Time (4.5)". Per frame this is a single dictionary lookup.
- Search: superseded ops are left out of placeholder search entirely (`continue`). Ops that only
  have the Obsolete tag keep the ×0.3 penalty. The library window still lists everything.
- Version label: an op with predecessors shows a small `v3` after its name in search results, the
  parameter window header and the help panel (v = number of predecessors + 1). Hovering the label
  shows the version history (each version's name, `Since` and `Changes`). From there you can
  insert an older version on purpose (for comparison, or to rebuild an old setup). The help window
  shows the same history as a section.
- Badge icon: `Icon.Flame` for now. An `ArrowTopRight` glyph ("update available") would be nicer
  but needs to be added to the icon atlas first; optional.
- SymbolLibrary: the filters "Outdated (n)" and "Depends on outdated ops" cover both superseded and
  Obsolete ops.
- A small "Supersedes" section in the parameter window's symbol settings (editable packages only)
  to edit `Inputs`/`Outputs` maps and `Changes`, using slot dropdowns of the old and new op.

### Phase 4: Project migration tool
- `OutdatedOperatorsScan`: one pass over editable symbols, collecting children whose symbol is
  outdated, plus `UnresolvedChildren` whose `SymbolId` has a successor.
- `OutdatedOperatorsDialog`, modelled on `MissingOperatorsDialog`:
  - grouped by package and composition;
  - per-row Show in Graph;
  - a lossy marker per op;
  - "Upgrade all" skips lossy upgrades by default and warns about them.
  - After the run, a navigator for skipped ops, like stepping through merge conflicts:
    "Lossy upgrade 3 of 9 · Time in [MyScene] · ◀ ▶". It jumps to each op in its graph and shows
    what would be lost and the `Changes` text, with "Upgrade anyway" and "Skip" buttons. It stays
    available from the project menu until the list is empty.
  - It produces one `MacroCommand` per run, so it can be undone. The pre-upgrade backup taken at
    each version change is the extra safety net.
- Entry points:
  - project menu "Upgrade outdated operators…";
  - a notice in `WelcomeWindow` (and `WelcomeAlphaWindow`) after a version change, shown only when
    the scan finds something.
  - No startup dialog, to avoid nagging.
- Usage counts: add unresolved children to `SymbolAnalysis.CollectSymbolUsageCounts`.

### Phase 5: Backfill the existing old ops
A one-off script, run with the editor closed:
- Pair each old op with its successor.
- Propose slot maps by name and type.
- Print the pairs for review, write the `Supersedes` records into the successors.
- Rename the old ops to `{Name}_v{N}` (class, files, cross-references listed above).

Old ops with no clear successor (e.g. `__ObsoletePulse`) keep only the Obsolete tag. Then migrate
all Lib and Examples uses with the Phase 4 tool, and compare renders through the debug bridge.

### Phase 6: Retirement
- CI or unit check: no symbol in Lib or Examples uses an outdated op.
- Upgrading deleted ops: add a small public read accessor on `UnresolvedChild` (input values,
  bypass, outputs) and for unresolved animation JSON, so Phase 2 can take its source data from an
  unresolved child. With that, deleting an old op's code no longer breaks migration.
- No fixed schedule: the developer decides to delete an old op. To prepare that decision, an
  agent can scan and suggest candidates, each with its usage count in Lib, Examples and loaded
  projects, its `Since` version, whether it has a successor, and whether the Phase 6 accessor
  makes deletion safe. Deletion comes with a release-notes entry.

### Phase 7 (only if needed): value converters
For changes in meaning that a slot map can't express (units, remapped enums). The likely form is a
static method on the new op class marked with an attribute naming the old symbol id. This needs a
tiny type in Core, so defer it until `Changes` texts and manual fixes prove too weak.

## Docs and Tests
- Operator conventions:
  - the naming rule (`{Name}_v{N}`, plain name = current);
  - "use Create New Version for breaking changes";
  - "map or drop every removed input";
  - add to AGENT_INSTRUCTIONS Operator Rules and to the wiki dev.OperatorConventions.
- `.help/` page "Upgrading outdated operators", linked from the dialog's help button.
- `.tests-manual/operator-upgrade.md` (badge, upgrade, undo, project tool, welcome notice). Release
  notes bullet.

## Decisions (2026-11-04)
1. **Random seeds change** after an upgrade: accepted. The upgrade report warns about it.
2. **Lossy upgrades in "Upgrade all"**: skipped by default, with a warning and a list to step
   through like merge conflicts ("3 of 9").
3. **Retirement**: done by hand by the developer. An agent can scan and suggest a list.
4. **Superseded ops are hidden from search.** Current ops show a `v3` label; hovering it shows the
   version history and offers to insert older versions. "Create new version" asks for a
   "What changed" text.
5. **Badge icon**: `Flame`. `ArrowTopRight` is optional later, once it exists in the icon atlas.
6. **Editor code that refers to old op ids**: logged as warnings. Updated by hand or by an agent
   in a separate commit.
