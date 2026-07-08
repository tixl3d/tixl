# Plan: Broken Package Recovery and In-Editor Backup Restore

## Motivation

Today the editor's startup is brittle in two ways:

1. **A single project that fails to compile blocks normal use of unrelated projects.** Crash example: a `.NET 9` user project that can't `dotnet restore` because the matching reference packs aren't installed. The editor still reaches the main loop, but creating a new project then dead-ends in `NewProjectDialog` ("This should never happen — file a bug report"), and the existing broken project is unusable with no in-editor remediation path.
2. **Recovering from corruption requires a manual zip extraction.** The startup-only crash dialog ([StartUp.cs](Editor/Gui/Interaction/StartupCheck/StartUp.cs)) only fires if the previous session left a `startingUp` lock file behind, and it restores *every* project's latest backup. There is no in-editor way to:
   - List backups for a specific project,
   - Pick an older backup,
   - Restore without restarting.

3. **Saving a symbol with unresolvable children silently destroys data.** Incident (2026-07-05): the new
   `Io` operator package was missing from the Release build's `PackageNames` list in Editor.csproj, so all
   its symbols (MidiInput, LoadDataClip, …) failed to resolve at load. `SymbolJson.TryReadSymbolChild`
   logs a per-child warning ("Error loading symbol child …") and **drops the child**; the user sees ops
   missing from graphs but gets no prominent alert. A subsequent "save all" then wrote the truncated
   symbols to disk — 24 children and their connections were permanently stripped from
   `Playground.t3`/`t3ui` and committed. Recovery was only possible because the projects folder happened
   to be a git repo. Any future package-loading bug (or a user opening a project on an install missing a
   package) repeats this as unrecoverable data loss.

Per-project incremental backups already live at `<projectFolder>/.temp/Backup/` (see [AutoBackup.cs](Editor/Gui/AutoBackup/AutoBackup.cs)), so the data layer is in place. This plan covers the UX and lifecycle on top.

## Progress

**2026-04-27** — NU1100 detection added: `Compiler.ExplainBuildFailure(string?)` ([Compiler.cs](Editor/Compilation/Compiler.cs)) recognises the missing-reference-pack error and returns a "install the matching .NET SDK / change the TFM" hint. Wired into [NewProjectDialog.cs](Editor/Gui/Graph/Dialogs/NewProjectDialog.cs) (replaces the "should never happen" dialog when the cause is known) and into the startup recompile log in [ProjectSetup.Startup.cs](Editor/Compilation/ProjectSetup.Startup.cs).

**2026-07-07** — Phase 2 step 1 landed (de-risked slice, no startup-lifecycle changes). See also
[Plan_PreUpgradeBackup](Plan_PreUpgradeBackup.md) for the pinned-backup primitive this builds on.
- `AutoBackup.EnumerateBackupsFor(projectFolder)` → `IReadOnlyList<BackupEntry>` (index, timestamp,
  path, size, isPinned), newest first ([AutoBackup.cs](Editor/Gui/AutoBackup/AutoBackup.cs)).
- `AutoBackup.RestoreFromArchive(projectFolder, zipPath)` extracted public from the private
  latest-restore; `RestoreLatestForProject` is now a thin wrapper.
- New `RestoreBackupDialog` ([RestoreBackupDialog.cs](Editor/Gui/Graph/Dialogs/RestoreBackupDialog.cs)):
  pick a version → labelled Restore CTA → result state telling the user to restart. Registered on
  `T3Ui`, drawn in the dialog loop.
- Wired into the Hub project context menu ("Restore from backup...") in
  [ProjectsPanel.cs](Editor/Gui/Hub/ProjectsPanel.cs) — available for any healthy or broken project.

**2026-07-07 (later)** — Restore flow reworked after UX review: the list-in-modal picker became a
"Restore from backup" **submenu** in the project context menu ([ProjectsPanel.cs](Editor/Gui/Hub/ProjectsPanel.cs)),
with scannable entries (`v231   23 min ago · 12 ops · 0.9 MB · pinned`; op count = `.t3` entries in
the zip, cached per path; labels cached, no per-frame IO). Picking one opens a small confirm dialog
([RestoreBackupDialog.cs](Editor/Gui/Graph/Dialogs/RestoreBackupDialog.cs)) with an
"Archive current state before restoring" checkbox (default on) that writes a pinned
`-keep-preRestore<timestamp>` backup first and aborts the restore if archiving fails. This also fixed
the dialog growing one frame at a time — its bottom-anchored list child (`Vector2(0, -h)`) fed back
into `ModalDialog`'s `AlwaysAutoResize`; the confirm dialog has intrinsic-height content and
[ModalDialog.cs](Editor/Gui/UiHelpers/ModalDialog.cs) now documents the constraint.

**2026-07-07 (evening)** — Restore now restarts the editor. Testing showed that after
`RestoreFromArchive` the running instance is unstable (per-frame "No release info found" spam — stale
assemblies and deleted bin/ under a live process), so the confirm CTA became **"Restore and Restart"**:
on success the dialog spawns a fresh editor process (same exe + original command-line args, so
`--override-version-id` survives) and exits via the plain exit path — which deliberately does *not*
save projects, since saving would overwrite the just-restored files with in-memory state. Verified:
no single-instance mutex exists, and `Program.cs` shutdown only disposes packages. If spawning fails,
the dialog falls back to "restored — please restart manually". Also added a startup low-disk-space
check ([StartUp.cs](Editor/Gui/Interaction/StartupCheck/StartUp.cs)): drives hosting the settings
folder or any project directory with <100 MB free trigger a blocking warning before anything writes.

**2026-07-07 (night)** — Restart-crash root cause found and fixed. The spawned instance died
silently at startup because `JsonUtils.TryLoadingJson` ([JsonUtils.cs](Serialization/JsonUtils.cs))
called `File.ReadAllText` **outside** its try/catch; when the exiting old instance held the settings
file mid-write (`TrySaveJson`/`File.CreateText`), the read threw `IOException` past the "Try" method
and killed the new process. Moved the read inside the try. To keep that fix from turning a crash into
*silent settings loss*, `Settings<T>` ([Settings.cs](Core/IO/Settings.cs)) now latches
`_preserveFileOnDisk` when an existing file fails to load, so save-on-quit won't overwrite the user's
real config with defaults. The `--wait-for-exit` handshake still eliminates the overlap on a clean
relaunch (bootstrap: the *spawning* build must have the flag-passing code), but the crash is now
non-fatal even without it.

**2026-07-08** — Restart confirmed working standalone; the earlier failures were Rider's debugger
killing the spawned child via its job object on parent exit. Hardened with `UseShellExecute = true`
([RestoreBackupDialog.cs](Editor/Gui/Graph/Dialogs/RestoreBackupDialog.cs)) so the child detaches from
the job and survives even under the debugger. Diagnosis used a temporary `StartUp.Probe`
facility writing to `startup-probe.log` plus milestone probes through `Program.Main`; in-Rider restart
was then confirmed and all probes were removed. Also found+fixed the underlying crash that made the
overlap fatal:
`JsonUtils.TryLoadingJson` read the file outside its try/catch, and `Settings<T>` now preserves an
unreadable file instead of clobbering it with defaults (see Plan_PreUpgradeBackup cross-refs).

Backup menu labels reworked: ".t3 count" is now labelled **symbols** (not "ops"), kept cheap (zip
index only) and stable in the label. **Operators** (symbol-children) and **connections** need
decompression, so they moved to a **hover tooltip** (also showing the absolute timestamp), counted
lazily — only for the hovered backup — on a background thread, cached per immutable zip path, shown as
"counting..." until ready. No label pop, no eager decompression of every backup. Keyframes deferred —
they live in a nested `Animator` block without an obvious per-keyframe key.

**2026-07-08** — Restore now clears stale files first ([AutoBackup.cs](Editor/Gui/AutoBackup/AutoBackup.cs)).
`RestoreFromArchive` previously only deleted bin/obj and extracted over the top, so an operator renamed
or deleted after the backup survived the restore and collided by Guid (same id, two files) with the
restored version. It now opens the archive first (corrupt-zip guard before any deletion), then deletes
the existing source/symbol/.meta files (`ClearGraphFilesBeforeRestore`, scoped via `IsMinimalBackupFile`)
before extracting. Assets/thumbnails are deliberately left untouched — they don't collide, and a minimal
backup wouldn't carry them back; this also means the minimal pre-restore archive fully covers what the
clear step could remove. Applies to crash-recovery restore too (same method). Dialog wording updated.

**2026-07-08 (audit)** — Adversarial review of the backup/restore paths surfaced four issues, all fixed
in [AutoBackup.cs](Editor/Gui/AutoBackup/AutoBackup.cs):
1. **Zip-slip** — the "escape the project folder" guard compared against the folder path without a
   trailing separator, so a sibling like `Proj_evil` prefix-matched `Proj`. Now compares against
   `WithTrailingSeparator(...)` and logs skipped entries. Matters once backups are shared/imported.
2. **Partial-restore safety** — `RestoreFromArchive` now pre-checks free disk space
   (`HasEnoughFreeSpaceToExtract`, sums zip entry sizes, fails open) and aborts *before* deleting
   anything; covers the crash-recovery caller which has no archive-first net.
3. **Silent large-file drop** — the 100 MB cap was applied to all backups. Now minimal-only (renamed
   `MinimalMaxFileSizeBytes`, logs when it skips); full backups keep everything so the "complete
   container" promise holds.
4. **Silent clear failure** — `ClearGraphFilesBeforeRestore` now returns success; if any stale file
   can't be removed (locked), the restore aborts instead of extracting over it and re-creating the
   Guid collision. `DeleteFile` returns a bool for this.

Lower-severity items noted but left: same-second double-restore false abort, pre-restore pinned archives
never pruned, `_isSaving` non-atomic, dedup timestamp cosmetic drift.

**2026-07-08 (Phase 1 core landed)** — Broken projects are now surfaced and recoverable instead of
silently dropped. `LoadProjects`' `failedProjects` (previously `out _`) is captured in `LoadAll`
([ProjectSetup.Startup.cs](Editor/Compilation/ProjectSetup.Startup.cs)); `ProjectLoadInfo` gained a
failure reason + hint set at the parse-fail and compile-fail sites (the latter reuses
`Compiler.ExplainBuildFailure`). New `ProjectSetup.BrokenProjectInfo` record + `BrokenProjects` list
([ProjectSetup.cs](Editor/Compilation/ProjectSetup.cs)), mirroring `ArchivedProjectInfo` (handles a null
`CsProjectFile` when the .csproj itself won't parse; not persisted, rebuilt each startup). The Hub shows
a "Broken" section ([ProjectsPanel.cs](Editor/Gui/Hub/ProjectsPanel.cs)) with a magenta attention bar,
the failure reason, the hint on hover, and a right-click menu → "Restore from backup" (the existing
submenu, now taking name+folder so it works without a loaded package) + "Reveal in Explorer". Restore →
restart picks up the recovered project. Purely additive to the load flow — loading was already lenient
(failures were dropped and the editor continued), so resilience is unchanged; this only makes the
failures visible and actionable.

**2026-07-08 (Phase 1 task 4 — data-loss guard landed)** — The save-truncates-data scenario is now
guarded. When `SymbolJson.TryReadSymbolChild` can't resolve a child (missing package), it increments
`Symbol.UnresolvedChildCount` ([Symbol.cs](../Core/Operator/Symbol.cs)) instead of only logging. The
editor then **refuses to overwrite that symbol's files** — `SaveSymbolFile`
([EditableSymbolProject.FileHandling.cs](../Editor/UiModel/EditableSymbolProject.FileHandling.cs))
returns early with a clear warning — so the intact on-disk copy (which still holds the child and its
connections) is never truncated. A startup summary (`WarnAboutUnresolvedChildren` in
[ProjectSetup.Startup.cs](../Editor/Compilation/ProjectSetup.Startup.cs)) reports "N operators across M
symbols could not be loaded" so the raw per-child Guid warnings aren't missed.

**Chose the refuse-to-save guard over the full round-trip** deliberately: the investigation confirmed
that connections to a dropped child are *pruned during instance creation*
([Instance.Connections.cs:77-83](../Core/Operator/Instance.Connections.cs)), so preserving just the
child JSON is insufficient — a correct round-trip would have to preserve child *and* connection JSON
and re-emit both in Core, where a mistake could write *worse* corruption. The guard is bulletproof and
minimal; the round-trip (let the user keep saving with unresolved children preserved as opaque blobs)
remains a future enhancement.

**2026-07-08 (corrupt-.t3 tolerance)** — A single corrupt `.t3` used to throw `FileCorruptedException`
through the parallel symbol load ([SymbolPackage.cs](../Core/Model/SymbolPackage.cs)), surface as an
`AggregateException`, and **abort the entire editor startup** (the "Loading Operators failed" dialog →
exit). Both read sites are now tolerant: `TryReadSymbolFile` (parse) and `ReadSymbolFromJsonFileResult`
(build) skip the bad file, log it, record it in `CorruptedSymbolFilePaths`, and loading continues.
`ReadAndCreate` wraps every read/parse error as `FileCorruptedException`, so catching it covers lock/IO
failures too. **Data-safety:** a corrupt symbol's type still exists in the compiled assembly, so the
symbol gets re-created *empty* from its type — to stop that empty version overwriting the (backup-
recoverable) file, `EditableSymbolProject` refuses to save any package with `CorruptedSymbolFilePaths`
(both `SaveAll` and the auto-save `SaveModifiedSymbols`). Startup summary via
`WarnAboutCorruptedSymbolFiles`. Composes with the missing-package guards above — same recovery path
(restore from backup), now robust at parse/build level too, not just compile level. The `.t3ui`
(SymbolUi) load path had the identical exposure — `EditorSymbolPackage.LoadUiFiles`
([EditorSymbolPackage.cs](../Editor/UiModel/EditorSymbolPackage.cs)) now uses a tolerant
`TryReadSymbolUiFile` that records into the same `CorruptedSymbolFilePaths` set (via the base's
`RecordCorruptedSymbolFile`), and the runtime hot-reload path (`Reload`) guards both its `ReadAndCreate`
calls so a file corrupted while the editor runs keeps the loaded version instead of throwing.

A project with corrupt files loads as an empty shell and stays in the *normal* Projects list (it's not
a failed-to-load "Broken" project), so it needs its own flag: `DrawProjectItem`
([ProjectsPanel.cs](Editor/Gui/Hub/ProjectsPanel.cs)) now shows a magenta `StatusAttention` bar and a
"N corrupt file(s) - restore from backup" line when `package.HasCorruptedSymbolFiles` (a cheap
`ConcurrentBag.IsEmpty`-based property, safe per-frame). The existing right-click "Restore from backup"
is the recovery path. Clicking a corrupt-file project is **blocked** (it would only open the empty shell)
with a log hint pointing at restore; a hover tooltip lists the corrupt file paths.

**Dependency-cascade (not done, needs design):** because a corrupt symbol is re-created *empty from its
type*, its Guid still resolves in the registry — so a dependent project referencing it does **not** get
`HasUnresolvedChildren` and isn't auto-flagged; it silently shows empty instances. Proper transitive
flagging would require marking the empty-from-corrupt symbol and propagating "references a damaged
symbol" to dependents (or switching to the guid-skip approach so the symbol is absent and dependents
go unresolved — but that leaves a corrupt *home* symbol's project homeless). Deferred as its own slice.

**Known tradeoff:** a project with a corrupt file can't save until reloaded (protected, not lost); and a
symbol with a permanently-missing package stays unsaveable for the session
(protected, not lost) — the fix is installing the package and reloading. **Still open (from Phase 1):**
the round-trip preservation (above) and dependency-cascade handling. A manual test set
(`.tests-manual/BrokenPackageRecovery/`) is still to be written.

**2026-07-08 (backup list markers)** — `BackupEntry` now carries `IsMinimal` + `KeepTag`
([AutoBackup.cs](Editor/Gui/AutoBackup/AutoBackup.cs)); the restore submenu
([ProjectsPanel.cs](Editor/Gui/Hub/ProjectsPanel.cs)) shows `· minimal` and a friendly keep-tag
(`pre-restore` / `pre-format-upgrade`) instead of a generic "pinned", and **mutes pre-restore rows**
(they snapshot the state being recovered from, so restoring one usually re-applies a bad state — the
exact #16 trap). Helps the user avoid restoring a backup that captured the corruption.

**Still open (Phase 2 step 2 / Phase 1):** capture per-project startup compile failures into a
`ProjectSetup.BrokenProjects` list (today `LoadProjects`' `failedProjects` is dropped as `out _`),
surface a "Broken" section in the Hub, and auto-open the restore dialog on failure. This is the part
that touches startup lifecycle (lenient loading, dependency-cascade risk) and wants deliberate testing.
Restore currently requires a manual restart (Phase 3 = in-place reload, still a stretch).

## Related plans

- [Plan_RuntimeConsistencyRecovery](Plan_RuntimeConsistencyRecovery.md) — the runtime-detection counterpart. Where this plan handles "package broken at load time" by routing it into a `BrokenProjectInfo`, the consistency-recovery plan handles "state observed to be corrupt at runtime" by suspending the editor and offering recovery. They share the backup-restore primitives in Phase 2 / 3 of this plan.
- [Plan_InstallVerificationAndSafeStartup](Plan_InstallVerificationAndSafeStartup.md) — handles TiXL's *own* deployed-file integrity (missing/corrupt DLLs, missing resources). Where this plan deals with user-project corruption, that plan deals with the installation underneath it. Both share the recovery dialog UX and the "Safe Startup" entry point.

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

4. **Preserve unresolved children instead of dropping them (data-loss guard).**
   - In `SymbolJson.TryReadSymbolChild` ([SymbolJson.cs:180](Core/Model/SymbolJson.cs:180)), when
     `SymbolRegistry.TryGetSymbol` misses, retain the child's raw JSON on the parent symbol as an
     *unresolved child* (id + original json blob) instead of discarding it.
   - **Save round-trips unresolved children verbatim** — a symbol saved while a package is missing keeps
     the children (and their connections) intact for when the package is back. This converts the failure
     mode from silent data loss to a benign placeholder.
   - Graph UI renders unresolved children as a distinct placeholder node ("missing op — package not
     loaded?") so the user notices immediately; a startup toast summarising "N children could not be
     resolved across M symbols" beats the current console-only warnings.
   - Cheaper interim guard, if the full round-trip is too invasive: refuse to save (or force a
     confirmation dialog on) any symbol that lost children during load.

### Notes / risks

- **Dependency cascade.** A broken package may be a compile-time dependency of other packages (e.g. a `Lib` consumer). Today `Lib` is loaded first and most user projects reference it; user projects rarely depend on each other, so the cascade is usually shallow. We should still verify by trying loading order with a deliberately broken project. If cascading hits, mark dependents as "broken (transitive)" rather than fail-loading them.
- **Sentry noise.** Today every recompile failure is logged via `Log.Error` and may end up in Sentry. With lenient loading, we should keep that telemetry but avoid duplicate reports per session.

### Manual test set

Add `.tests-manual/BrokenPackageRecovery/` covering:
- Project that fails NuGet restore (induce by adding a non-existent package reference).
- Project whose `.cs` has a syntax error.
- Project whose dependent `Lib` types changed underneath it.
- Project using ops from a package that isn't loaded (induce by temporarily removing a package from
  the Release `PackageNames` list): placeholders shown, saving and reloading with the package restored
  brings the ops back intact.
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
