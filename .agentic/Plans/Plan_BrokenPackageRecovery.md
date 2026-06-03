# Plan: Broken Package Recovery and In-Editor Backup Restore

## Motivation

Today the editor's startup is brittle in two ways:

1. **A single project that fails to compile blocks normal use of unrelated projects.** Crash example: a `.NET 9` user project that can't `dotnet restore` because the matching reference packs aren't installed. The editor still reaches the main loop, but creating a new project then dead-ends in `NewProjectDialog` ("This should never happen — file a bug report"), and the existing broken project is unusable with no in-editor remediation path.
2. **Recovering from corruption requires a manual zip extraction.** The startup-only crash dialog ([StartUp.cs](Editor/Gui/Interaction/StartupCheck/StartUp.cs)) only fires if the previous session left a `startingUp` lock file behind, and it restores *every* project's latest backup. There is no in-editor way to:
   - List backups for a specific project,
   - Pick an older backup,
   - Restore without restarting.

Per-project incremental backups already live at `<projectFolder>/.temp/Backup/` (see [AutoBackup.cs](Editor/Gui/AutoBackup/AutoBackup.cs)), so the data layer is in place. This plan covers the UX and lifecycle on top.

## Progress

**2026-04-27** — NU1100 detection added: `Compiler.ExplainBuildFailure(string?)` ([Compiler.cs](Editor/Compilation/Compiler.cs)) recognises the missing-reference-pack error and returns a "install the matching .NET SDK / change the TFM" hint. Wired into [NewProjectDialog.cs](Editor/Gui/Graph/Dialogs/NewProjectDialog.cs) (replaces the "should never happen" dialog when the cause is known) and into the startup recompile log in [ProjectSetup.Startup.cs](Editor/Compilation/ProjectSetup.Startup.cs).

## Related plans

- [Plan_RuntimeConsistencyRecovery](Plan_RuntimeConsistencyRecovery.md) — the runtime-detection counterpart. Where this plan handles "package broken at load time" by routing it into a `BrokenProjectInfo`, the consistency-recovery plan handles "state observed to be corrupt at runtime" by suspending the editor and offering recovery. They share the backup-restore primitives in Phase 2 / 3 of this plan.

## Related Sentry issues

These crashes are symptoms of the same root problem this plan addresses (partially-loaded packages reaching code that assumes a fully-valid Symbol graph). Phase 1's startup consistency check should catch them at the load boundary rather than letting them surface downstream:

- **[TOOLL3-YE](https://tooll.sentry.io/issues/7514716102/)** — `Symbol.get_Namespace()` NRE during `SymbolLibrary` tree population at startup. A Symbol with null `InstanceType` survives into `EditorSymbolPackage.AllSymbolUis` and crashes the OrderBy in [NamespaceTreeNode.PopulateCompleteTree](Editor/Gui/Windows/SymbolLib/NamespaceTreeNode.cs:69). The accessor itself is not the bug — a Symbol in this state is corrupt and any null-safe band-aid just defers the crash to graph rendering or save. Phase 1 should reject the malformed Symbol during package load (record as `BrokenProjectInfo` with category `SymbolLoadFailed`) before it reaches the global symbol-UI list. Also a trip-source for [Plan_RuntimeConsistencyRecovery](Plan_RuntimeConsistencyRecovery.md) Phase 2 if a malformed Symbol slips past the load-time check.

---

## Phase 1: Lenient package loading

**Goal:** A project that fails to compile or fails to load no longer prevents the rest of the editor from working. It is recorded as a "broken" package, similar to how archived projects are handled.

### Tasks

1. **Introduce a `BrokenProjectInfo` record in the same spirit as `ArchivedProjectInfo`.**
   - Holds the `CsProjectFile`, the failure category (`CompileFailed`, `RestoreFailed`, `AssemblyLoadFailed`, `SymbolLoadFailed`), the failure message, and the optional `Compiler.ExplainBuildFailure` hint.
   - Stored on `ProjectSetup` alongside `ArchivedProjects`.

2. **Wrap each per-project step in [ProjectSetup.Startup.cs](Editor/Compilation/ProjectSetup.Startup.cs) so a failure produces a `BrokenProjectInfo` instead of cascading.**
   - Currently the recompile failure already returns `ProjectLoadInfo(..., success: false)`. Extend that path to also record the cause (with the explanation) so later UI can surface it.
   - Today the symbol-load and child-resolve paths swallow individual failures as warnings (the existing "Error loading symbol child …" messages); promote *project-scope* failures to the new `BrokenProjectInfo` collection rather than silently dropping the package.

3. **Surface broken projects in the project list UI.**
   - The existing project picker / dropdown lists user projects. Show broken ones with a distinct visual treatment (warning glyph + reason on hover) — same affordance as the existing archived-project styling.
   - Selecting a broken project shows a side panel: failure category, the explanation hint (if any), and the action buttons described in Phase 2.

### Notes / risks

- **Dependency cascade.** A broken package may be a compile-time dependency of other packages (e.g. a `Lib` consumer). Today `Lib` is loaded first and most user projects reference it; user projects rarely depend on each other, so the cascade is usually shallow. We should still verify by trying loading order with a deliberately broken project. If cascading hits, mark dependents as "broken (transitive)" rather than fail-loading them.
- **Sentry noise.** Today every recompile failure is logged via `Log.Error` and may end up in Sentry. With lenient loading, we should keep that telemetry but avoid duplicate reports per session.

### Manual test set

Add `.tests-manual/BrokenPackageRecovery/` covering:
- Project that fails NuGet restore (induce by adding a non-existent package reference).
- Project whose `.cs` has a syntax error.
- Project whose dependent `Lib` types changed underneath it.
For each: editor still reaches main loop, broken project shown with explanation, other projects usable.

---

## Phase 2: In-editor backup browser & restore

**Goal:** Right-click any project (broken or healthy) → "Restore from backup" → modal lists available backups for that project, user picks one, editor restores.

### Tasks

1. **Refactor `AutoBackup.RestoreLatestForProject(projectFolder)` ([AutoBackup.cs](Editor/Gui/AutoBackup/AutoBackup.cs)) to accept an explicit zip path.**
   - New: `RestoreFromArchive(string projectFolder, string zipFilePath) → bool`.
   - `RestoreLatestForProject` becomes a thin wrapper that resolves the latest zip then calls `RestoreFromArchive`.
   - The existing `bin/`/`obj/` cleanup stays in `RestoreFromArchive`.

2. **Add `AutoBackup.EnumerateBackupsFor(projectFolder)` returning a sorted list of `(int index, DateTime timestamp, string fullPath, long sizeBytes)` for every zip in `<projectFolder>/.temp/Backup/`.**
   - Reuse the existing `_backupNameRegex` parser.
   - Newest first.

3. **New `BackupBrowserDialog` (modal) under `Editor/Gui/Graph/Dialogs/`.**
   - Inputs: target project folder + display name.
   - Table: index, timestamp, age, size, "Restore" button per row.
   - Confirm modal before destructive restore.
   - Uses existing `FormInputs` / `CustomComponents` patterns; no new infrastructure.

4. **Hook into the project list context menu** (right-click on a project entry) and into the "broken project" side panel from Phase 1.

### Notes / risks

- The "before destructive restore" prompt must spell out: "this will delete `<project>/bin/` and `<project>/obj/` and overwrite the project's source files with the contents of the chosen zip." Users should not lose work to a misclick.
- Restore should run on a background `Task` (like the backup write) so the UI doesn't block on large zips.

---

## Phase 3: Restore without restarting (stretch)

**Goal:** After restore, reload the affected package's symbols/UI/assembly in place. No "please restart" message.

### Why this is the risky one

- TiXL has hot reload, so per-package assembly-unload + reload primitives exist (see `EditableSymbolProject` and the `AssemblyLoadContext` usage in `ProjectSetup`).
- However, the current package loading path is mostly a single-shot startup batch. Per-project hot reload of a *changed-on-disk* project (post-restore) needs:
  1. Unload the package's assembly + symbol package + symbol UIs.
  2. Apply the disk changes (already done by `RestoreFromArchive`).
  3. Re-run `dotnet restore`/build for that one project.
  4. Reload the symbol package; re-resolve `SymbolChild` references in *other* packages that pointed at this one.
  5. Refresh any open `ProjectView` / `MagGraphCanvas` that was viewing the restored project.

- Step 4 is the load-bearing one: cross-package `SymbolChild` references are by `Guid`. If the restored zip's `Symbol` `Guid`s match what was on disk before, dependents stay valid. If `Symbol` `Guid`s differ (e.g., user restored across a refactor that renamed/replaced ops), some dependents become broken — same lenient-load semantics as Phase 1 should kick in.

### Suggested approach

1. **First milestone:** support no-restart restore *only if* the target project has no other open projects that import its symbols. Detect, and otherwise message: "Restore queued. The editor needs to restart to apply." This is the safest opt-in.
2. **Later milestone:** generalise to cross-package reload by walking dependents and reloading them in topological order.

### Risks

- AssemblyLoadContext unload is asynchronous and best-effort; a stuck reference (typically held by ImGui state, or a pinned tooltip) prevents collection. We need a fallback "apply on restart" path even when the optimistic path is taken.
- Open `ProjectView` / `MagGraphCanvas` instances cache `Symbol` and `SymbolUi` references. They must be told to drop and re-resolve.

---

## Out of scope

- Migrating the legacy `%APPDATA%/TiXL<ver>/Backup/` archives into the new per-project layout. (User decided to leave those alone.)
- Preventing future NU1100-class failures (e.g. shipping the required reference packs alongside TiXL). The current detection just reports the cause.
- Cross-machine restore (importing a backup zip from another user's TiXL install).

---

## Order of work

1. Phase 1 (lenient loading + broken-project surface) — biggest UX win, no risky lifecycle work.
2. Phase 2 (backup browser dialog + per-zip restore) — enables manual recovery without restart-loops.
3. Phase 3 (in-place reload) — stretch; only after the above are stable.

## Open: which slice to land first

Two reasonable starting points; pick one before coding tomorrow.

**Option A — Phase 1 first.** Directly unblocks the JW-style "one broken project locks me out" scenario. Higher risk because it touches `ProjectSetup` startup lifecycle and dependent-package resolution. Best when the immediate user pain is "broken projects".

**Option B — Phase 2 sub-slice first.** Smallest self-contained ship: refactor [AutoBackup.RestoreLatestForProject](Editor/Gui/AutoBackup/AutoBackup.cs) to take an explicit zip path, add `EnumerateBackupsFor`, build the `BackupBrowserDialog` and wire it into a temporary menu entry for testing (defer the project-list context menu and the broken-project side panel until Phase 1 lands). Self-contained, low lifecycle risk, immediate user value (recover an older backup without manual zip extraction), groundwork for Phase 1's restore button.

Recommendation: **Option B** unless someone is currently blocked by a broken project. Phase 1 lands cleaner once Phase 2's restore primitives exist (the broken-project panel can then call them directly).
