# Plan: Pre-Upgrade Backup for Breaking File-Format Changes

## Motivation

The v4.2 file format is changing (`SymbolFormatVersion.Current` is now `3`, see
[SymbolFormatVersion.cs](../Core/Model/SymbolFormatVersion.cs)). If a user later needs to
return to an earlier build — a regression, a breaking change they can't live with — they
must not lose data.

The subtle part: **the data-loss risk lives in the *old* editor, not in v4.2.** When a
pre-4.2 editor opens a file written by v4.2, `SymbolFormatVersion.WarnIfNewer`
([SymbolFormatVersion.cs:35](../Core/Model/SymbolFormatVersion.cs:35)) only *logs a
warning*. If that older loader then drops fields it doesn't understand and the user saves,
the newer data is gone — and nothing shipped in v4.2 can prevent it, because the damage is
done by already-released code.

Since we can't fix the old editor retroactively, the only reliable insurance is **a
durable copy of each project's files taken before v4.2 first touches them.** Reverting to
that copy *is* the backward-migration path — which is why this plan does **not** build a
v4.2 → v4.1 downgrade converter (expensive, hard to test, and inherently lossy for
genuinely new concepts).

## Progress

**2026-07-07** — Phase 1 landed. `AutoBackup` ([AutoBackup.cs](../Editor/Gui/AutoBackup/AutoBackup.cs))
now supports pinned (`-keep-<tag>`) backups: regex extended (group 10), `ReduceNumberOfBackups`
skips pinned indices, `TouchLatestBackupTimestamp` preserves the marker on dedup renames, and
`CreatePinnedBackup(projectFolder, tag)` writes a full, prune-proof snapshot (no-op if the tag
already exists). Editor builds clean. Phases 2 (snapshot on first v4.2 launch) and 3 (welcome
notice + project-list `EditorVersion`) still open.

## Related plans

- [Plan_BrokenPackageRecovery](Plan_BrokenPackageRecovery.md) — its Phase 2/3 is the
  in-editor "backup browser & restore" feature. The pinned-backup mechanism in Phase 1
  below is the primitive that feature will surface: the browser lists backups, and a
  "pin / keep" affordance marks any state as protected-from-pruning. The pre-upgrade
  snapshot is just the first *producer* of a pinned backup; the restore UI is the
  first *consumer*. Reverting to the pinned snapshot in that UI is what completes the
  "migrate back to 4.1" story.

## Background: the two version fields

There are two distinct version concepts, and they play different roles here:

- **`EditorVersion`** — written into each project's `.csproj` on every save via
  `Program.Version` ([CsProjectFile.cs:359](../Editor/Compilation/CsProjectFile.cs:359)),
  parsed into `ReleaseInfo.EditorVersion` (a real `Version`,
  [ReleaseInfo.cs](../Core/Compilation/ReleaseInfo.cs)). **Per-project**, already loaded,
  cheap to read. This is the right signal for the "did this project predate v4.2?" gate
  and for the project-list display.
- **`FormatVersion`** (int) + `TixlVersion` string — embedded per `.t3`/`.t3ui` file, and
  what `WarnIfNewer` checks. **Per-file**, finer grained.

For the cross-major boundary we care about ("pre-4.2 vs 4.2"), `EditorVersion < 4.2` is a
clean, cheap gate with a useful self-clearing property: it still reads `< 4.2` right up
until v4.2's first save rewrites it, so "snapshot when `EditorVersion` is below the running
`major.minor`, before the first save" needs no separate per-project marker.

**Deliberately out of scope: intra-version (alpha) format bumps.** `FormatVersion` 2 and 3
both shipped under editor "4.2.0.2", so `EditorVersion` can't distinguish them. That's
fine — those are separated alpha releases, and anyone hand-editing across them should be on
git or another VCS. This plan only guards the cross-major upgrade.

---

## Phase 1: Pinned ("keep") backups in AutoBackup

**Goal:** A backup zip can be marked so the pruner never deletes it, while it otherwise
participates in the normal backup timeline (index counting, "latest" resolution, timestamp
parsing, restore). This is a general mechanism, not a pre-upgrade special case.

### Filename marker

Keep the existing schema and *add* an optional suffix rather than inventing a parallel
naming scheme — a uniform scheme is what lets the future backup browser enumerate every
backup (pinned or not) through one parser.

- Current: `#{index:D5}-{yyyy_MM_dd-HH_mm_ss_fff}{-minimal}?.zip`
- Pinned:  `#{index:D5}-{yyyy_MM_dd-HH_mm_ss_fff}{-minimal}?{-keep-<tag>}?.zip`
- `<tag>` is `[A-Za-z0-9.]+` — letters, digits, dots only, **no hyphens** (hyphen is the
  field separator, so a hyphen in the tag would break parsing). Encodes the reason, e.g.
  `-keep-pre4.2`. Reserve the general shape for future producers (`-keep-userPinned`,
  `-keep-preRestore`).

### Tasks (all in [AutoBackup.cs](../Editor/Gui/AutoBackup/AutoBackup.cs))

1. **Extend `_backupNameRegex`** ([line 570](../Editor/Gui/AutoBackup/AutoBackup.cs:570))
   with an optional trailing group after `(-minimal)?`, e.g. `(-keep-[A-Za-z0-9.]+)?`.
   This is what keeps a pinned zip parseable so index/timestamp/"latest" logic keeps
   working for it.

2. **Guard the prune in `ReduceNumberOfBackups`**
   ([line 492](../Editor/Gui/AutoBackup/AutoBackup.cs:492)) — when a matched name carries
   the keep group, skip the `DeleteFile`. That is the entire "never prune" behavior.

3. **Fix the timestamp-touch trap in `TouchLatestBackupTimestamp`**
   ([line 228](../Editor/Gui/AutoBackup/AutoBackup.cs:228)). On a dedup no-op it *renames*
   the latest archive, reconstructing the name from `#{index}-{ts}{minimalSuffix}.zip` —
   which **drops any suffix it didn't capture**. Right after a pinned snapshot is created
   it *is* the latest, so if the next full backup is byte-identical (user opened but didn't
   edit, `MinimalBackup=false`), this would silently strip the keep marker and lose the
   pin. Preserve the captured keep group in the reconstructed name (or skip touching pinned
   files entirely).

4. **Add a small helper to create a pinned snapshot**, e.g.
   `AutoBackup.CreatePinnedBackup(string projectFolder, string tag)`:
   - Always a **full** backup (never minimal), so the pin is a complete copy.
   - Slots in at the next index like a normal backup (so it becomes `#00001-…-keep-pre4.2`
     when it's the project's first backup, and the next auto-backup continues at `#00002`).
   - No-op if a `-keep-<tag>` backup with the same tag already exists for the project
     (covers upgrade → downgrade → re-upgrade without duplicating).

### Notes

- Because every enumeration path (`GetLatestArchiveFilePath`, `GetIndexOfLastBackup`,
  `ReduceNumberOfBackups`, `ParseTimestampFromName`) is gated on a regex match, extending
  the regex is what makes the pinned backup a first-class citizen everywhere at once.
- Crash-recovery restore (`RestoreLatestBackups`) restores the *highest* index, so a
  pinned `#00001` is only ever auto-restored if it's literally the only backup — harmless.

---

## Phase 2: Take the snapshot on first v4.2 launch

**Goal:** The first time a v4.2 build runs over projects written by an older editor, each
such project gets one pinned full snapshot before v4.2 can rewrite it.

### Tasks

1. **Trigger on `NewToUser`.** `VersionMarker.Classify()`
   ([VersionMarker.cs](../Editor/Gui/Dialog/VersionMarker.cs)) already distinguishes
   `NewToUser` / `Downgrade` / `Silent`. On `NewToUser`, before the auto-backup loop or any
   save runs, iterate projects and for each whose `ReleaseInfo.EditorVersion` is below the
   running `major.minor`, call `AutoBackup.CreatePinnedBackup(folder, "pre4.2")`.
   - Guard on the marker file's presence (Phase 1 task 4) so it's genuinely one-time.
   - Collect the list of `(projectName, absoluteZipPath)` created, for the Phase 3 notice.

2. **Migrate lazily, on save — never mass-rewrite on launch.** Do *not* rewrite every
   project to the new format at startup: that's scary (mass file churn, git noise) and
   fragile (a crash mid-migration leaves a half-converted state — the exact class of
   "recompile interferes with save" bug just fixed in `6dff0ac45`). A project's files are
   upgraded only when the user opens *and* saves it. The pinned snapshot is taken before
   that first save. A user who only browses keeps their files untouched.

### Notes / risks

- **`.temp` durability caveat.** The snapshot lives under `<project>/.temp/Backup/` like
  every other backup. If a user deletes `.temp` or tooling cleans it, the net is gone.
  Acceptable — `.temp` is the natural home — but the Phase 3 notice must show the
  **absolute path** so a cautious user can copy it somewhere durable.
- Placement under `.temp` also means the snapshot is excluded from other backups' contents
  and from git (both already exclude `.temp`), and normal restores (which only wipe
  `bin`/`obj`) leave it intact.

---

## Phase 3: Tell the user, and show project versions

**Goal:** The user understands that an upgrade happened and where the safety copy is, and
can see at a glance which projects predate their editor.

### Tasks

1. **WelcomeWindow notice.** In the `NewToUser` welcome flow
   ([WelcomeWindow.cs](../Editor/Gui/Dialog/WelcomeWindow.cs)), when Phase 2 produced
   snapshots, show a short line: "N project(s) were upgraded to the 4.2 format. A backup of
   the previous version was saved to `<absolute path>`." Only show it when snapshots were
   actually taken — no noise for users with nothing to migrate.

2. **Show `EditorVersion` in the project list.** Next to each project's path/name (the
   project picker and the WelcomeWindow "Projects" tab / Hub), display
   `ReleaseInfo.EditorVersion`. It's already loaded per project. This quietly primes users
   to notice when a project predates their current editor, and it's the natural anchor for
   the future backup-browser entry point (see [Plan_BrokenPackageRecovery](Plan_BrokenPackageRecovery.md)).
   - Reuse existing `CustomComponents` / styling helpers; no new widget infrastructure.

3. **Release notes.** Ensure `.help/release-notes/v4.2.md` mentions the format change and
   that a pre-upgrade backup was made (the WelcomeWindow surfaces these notes).

---

## Out of scope

- **A v4.2 → v4.1 downgrade converter.** The pinned snapshot is the backward path; a
  reverse converter is expensive, hard to test, and lossy for new concepts.
- **Intra-version (alpha) format bumps** — separated releases; use git.
- **Eager mass migration at startup** — replaced by lazy migrate-on-save.
- **The in-editor backup browser / restore UI itself** — owned by
  [Plan_BrokenPackageRecovery](Plan_BrokenPackageRecovery.md). This plan only provides the
  pin primitive it will consume.

---

## Order of work

1. **Phase 1** (pinned-backup mechanism) — self-contained, testable in isolation, and the
   reusable primitive. Land first.
2. **Phase 2** (snapshot on first v4.2 launch) — depends on Phase 1's `CreatePinnedBackup`.
3. **Phase 3** (welcome notice + project-list version) — pure UI, depends on Phase 2's
   result list; lands last.

## Manual test set

Add `.tests-manual/PreUpgradeBackup/` (with `added:` / `added-in-version:` frontmatter):

- Open a project last saved by a pre-4.2 editor with a fresh v4.2 install (simulate via
  `versionMarker.json` absent/old and a `.csproj` with an older `EditorVersion`): a
  `#…-keep-pre4.2.zip` appears in `.temp/Backup/`, and the WelcomeWindow reports the path.
- Let auto-backup run many times: the `-keep-` snapshot survives pruning while ordinary
  backups thin out.
- Open-but-don't-edit a legacy project: files on disk are *not* rewritten (lazy migration).
- Upgrade → (manually) restore the pinned zip → confirm the project loads in the older
  build without lost data.
