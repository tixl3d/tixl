# Plan: Alpha Version Separation & Version-Aware Welcome

## Motivation

TiXL ships its next-version work on `main` immediately after each stable release branch is cut — so `main` is always an "alpha of the upcoming version". Over the last few cycles this has bitten users in three ways:

1. **Folder collision.** Alpha and stable of the same `major.minor` write to the same user folders (`%APPDATA%\TiXL4.2\`, `~\Documents\TiXL4.2\`). An alpha that introduces a settings-format change, breaks a layout, or rewrites a project on save can corrupt the matching stable install — or vice versa.
2. **No alpha onboarding.** Users who run an alpha for the first time don't see a "this is alpha, here's what's new, here's where your data went, here's how to import from stable" path. They just find an empty TiXL with none of their projects.
3. **No version-bump signal in stable.** A user who upgrades from 4.3.0 to 4.3.1 (or from 4.2 to 4.3) has no in-editor way to know what changed. Release notes live on GitHub and are easy to miss.

The current state is "half-baked": folder names already include the version (`TiXL4.2`), but the `-alpha` suffix from `Tixl.props` is plumbed into Editor's `InformationalVersion` only and never reaches the folder name or any runtime API. There is no `IsAlpha` property to read, and no record of which version last ran.

This plan formalizes the split — separate folders for alpha vs stable, a single `IsAlpha` source of truth, a persisted `lastRunVersion` that drives both a first-run welcome (cold install) and a compact "what's new" popup (in-place version bump), and a Manual Features Tests sort-by-recency convention so testers can find new things to try.

## Progress

**2026-05-31** — Phase 1 landed:
- `VersionSuffix` + `InformationalVersion` plumbed into [`Core.csproj`](../Core/Core.csproj) so the prerelease segment is preserved in Core's `AssemblyInformationalVersionAttribute`.
- [`Core/Compilation/RuntimeAssemblies.cs`](../Core/Compilation/RuntimeAssemblies.cs) now exposes `VersionSuffix`, `IsAlpha`, `FormattedVersion`. Parses the semver from Core's `AssemblyInformationalVersionAttribute`; `IsAlpha` is permissive so future `beta`/`rc` cycles inherit the same isolation. Also: static fields reordered into source-order dependency so initialisers don't observe `null`.
- [`Core/Settings/FileLocations.cs`](../Core/Settings/FileLocations.cs) now reads through `RuntimeAssemblies` instead of doing its own reflection; new `VersionedAppFolderName` constant (`TiXL4.2-alpha` or `TiXL4.2`) feeds both `SettingsDirectory` and `DefaultProjectFolder`. The pre-existing public `TixlVersion` (used by `IoDataSetRecorder` for recording-metadata provenance) keeps its `"4.2"` shape — the suffix lives only in the folder name.
- Manual test set added at [`.tests-manual/alpha-folder-separation.md`](../.tests-manual/alpha-folder-separation.md).
- Build verified clean on Core, Editor, and Player (only pre-existing `NU1701` package warnings remain).

Phase 2 (`VersionMarker` + welcome popup) is unblocked.

## Non-goals

- Auto-generating release notes from commits. Worth doing later; not in v1. Release notes for v1 are hand-written by the release-cut author into per-version markdown files.
- Migrating Tooll3 (v3.x) data. Out of scope.
- Changing how Player resolves its own content folder (exported games stay self-contained next to the exe).
- Cross-version project format compatibility. The plan assumes the existing back-compat readers continue to handle older saved data.
- An auto-update mechanism. The popup tells the user what changed *after* they upgrade; it does not download or install anything.

## Current wiring (reference)

- **Version source of truth:** [`Tixl.props`](../Tixl.props) — `TixlVersion=4.2.0.2`, `TixlVersionSuffix=alpha`. Manually edited at release-cut time.
- **Editor** ([`Editor.csproj`](../Editor/Editor.csproj)) plumbs both into `VersionPrefix` + `VersionSuffix` and emits `InformationalVersion` as semver (`4.2.0.2-alpha+sha`). [`Program.FormattedEditorVersion`](../Editor/Program.cs) parses this for the about/title display.
- **Core** ([`Core.csproj`](../Core/Core.csproj)) currently plumbs only `VersionPrefix`. Core's own `InformationalVersion` does **not** carry the suffix.
- **`RuntimeAssemblies.Version`** ([`Core/Compilation/RuntimeAssemblies.cs`](../Core/Compilation/RuntimeAssemblies.cs)) reads `_coreAssembly.GetName().Version` — `Major.Minor.Build.Revision` only, suffix lost.
- **`FileLocations`** ([`Core/Settings/FileLocations.cs`](../Core/Settings/FileLocations.cs)) builds `SettingsDirectory` and `DefaultProjectFolder` from `GetAssemblyVersion()` → `"4.2"`. No suffix.
- **Player** ([`Player/Program.cs`](../Player/Program.cs)) reads `FileLocations.SettingsDirectory` only for its log path. Exported content lives at `StartFolder` next to the exe and is unaffected.

All other `FileLocations.SettingsDirectory` consumers (themes, keybindings, layouts, recordings, skill progress, tests, gradients, auto-backup lock file) inherit the folder name automatically — no per-consumer migration needed.

## Resolved decisions

1. **Welcome surface — resolved.** Modal popup styled like the existing `Help → About TiXL` dialog. `Help → Welcome` reopens it at any time (alpha and stable).

2. **Settings import — resolved.** A user-facing checklist of categories, each independently toggleable:
   - `[ ] Settings` — the curated allowlist of safe `userSettings.json` keys (`KeyBindingName`, recent files, `UiScaleFactor`, misc small prefs that don't drift). The umbrella checkbox; granular per-key control is unnecessary.
   - `[ ] Layouts` — copy the `Layouts/` subfolder. Excluded from "Settings" because layouts drift between versions; users sometimes want them anyway.
   - `[ ] Themes` — copy `Themes/` subfolder.
   - `[ ] Keymaps` — copy `KeyBindings/` subfolder.
   All checkboxes default off; user opts in explicitly. The dialog shows the source folder so the user sees where each category is coming from.

3. **Project import — resolved.** Copy only. No link option. Copying leaves the source projects untouched and avoids the "newer version writes a format the old version can't read" footgun. Drop the link-mode UI, banner, and `ProjectDirectories` plumbing from earlier drafts.

4. **`lastRunVersion` mechanism — resolved.** Persist `versionMarker.json` in `SettingsDirectory`; classify each launch as `Silent` / `ColdStart` / `VersionBump` / `Downgrade` and dispatch from there.

5. **Release notes location — resolved.** Per-version files under `release-notes/<version>.md`, frontmatter `version`, `date`, `highlights[]`, body is full markdown. Alpha uses a rolling `release-notes/alpha.md`.

6. **`VersionSuffix` lifecycle — resolved.** Workflow as proposed (stable cut empties suffix, `main` re-adds `alpha`). Add a one-line note to the release process. **Where:** the project's release process lives on the GitHub wiki (`dev.Contributing` or equivalent — confirm path during implementation), not in-repo — `.help/docs/contributing/README.md` defers all developer process to the wiki. Add the note there.

7. **Trigger model — resolved.** One popup per build type, both fired by the same condition: `lastRunVersion != current` (missing or lower). `IsAlpha` switches the content variant. The cold-start vs bump split is internal to the dialog — the import section is conditional on the current folder being empty of prior data, not on a separate phase.

8. **Manual tests `added:` field — resolved.** Frontmatter date on each test set. Implementation lands in Phase 3.

---

## Phase 1: `IsAlpha` source of truth + folder split

**Goal:** A single `IsAlpha` API, the `-alpha` suffix folded into the user-folder names, and Player inheriting it for free.

### Tasks

1. **Plumb `VersionSuffix` into Core.** Add to [`Core.csproj`](../Core/Core.csproj), mirroring the lines already in `Editor.csproj`:
   ```xml
   <VersionSuffix>$(TixlVersionSuffix)</VersionSuffix>
   <InformationalVersion>$(Version)</InformationalVersion>
   ```
   Without this, the suffix is editor-only and `RuntimeAssemblies` (which reads Core) can't see it.

2. **Extend `RuntimeAssemblies`** ([`Core/Compilation/RuntimeAssemblies.cs`](../Core/Compilation/RuntimeAssemblies.cs)). Parse Core's `AssemblyInformationalVersionAttribute` once at static init; expose:
   - `Version` *(existing, unchanged — `Major.Minor.Build.Revision`)*
   - `VersionSuffix` — the prerelease segment of semver (e.g. `"alpha"`, `"beta.2"`, `""` when stable). Defined as everything between `-` and `+` in the informational version.
   - `IsAlpha` — `!string.IsNullOrEmpty(VersionSuffix)`. Intentionally permissive: any prerelease segment counts as "not stable", so future `beta`/`rc.1` cycles work without a code change.
   - `FormattedVersion` — `"4.2.0.2-alpha"` (no SHA, no Debug suffix). Moves the parsing logic out of `Editor/Program.cs` so Player and operators can read it too. `Program.FormattedEditorVersion` becomes a thin wrapper that adds `+sha` and `Debug`.

3. **Fold suffix into folder naming.** Update [`FileLocations.GetAssemblyVersion()`](../Core/Settings/FileLocations.cs) to append `-{VersionSuffix}` when present. Apply to **both**:
   - `SettingsDirectory` → `%APPDATA%\TiXL4.2-alpha\`
   - `DefaultProjectFolder` → `~\Documents\TiXL4.2-alpha\`

4. **Verify no downstream consumer hard-codes the old name.** A grep for `"TiXL4."` or `AppSubFolder` literals will catch anything that built its own copy of the folder name.

### Why `RuntimeAssemblies` and not `App`/`Program`

`App`/`Program` is editor-only. Operators run inside Player as well as Editor, and the Player log path now also wants to know. `RuntimeAssemblies` is the existing version singleton in Core, reachable from everywhere, with no dependency-cycle exposure.

### Player implications

Player's only touchpoint is its log path ([`Player/Program.cs:93`](../Player/Program.cs)) which already routes through `FileLocations.SettingsDirectory`. Alpha-built Players will log under `TiXL4.2-alpha\Player\...` automatically. **No Player code change needed in Phase 1.** A separate question — should a stable Player be able to load content exported from alpha? — is a content-compat question, not a settings-folder one, and is out of scope.

### Risks

- **Existing alpha users get a fresh empty folder on first launch after this lands.** Their previous alpha data sits orphaned in `TiXL4.2\` and looks like a stable install. Phase 2's welcome screen + import mitigates this; until Phase 2 lands they're stuck. Mitigation: announce the change in the release notes for the alpha that introduces it, and ship Phases 1+2 in the same release.
- **`ProjectDirectories` in `UserSettings` stores absolute paths.** When Phase 2's "import settings" runs, the stable's project directory list contains paths like `C:\Users\X\Documents\TiXL4.2\foo`. We need to decide per-project whether to import as-is (shared with stable) or copy into the alpha tree. See open question 3.

### Manual test set

Add `.tests-manual/alpha-folder-separation.md` covering:
- Fresh alpha install → folders are `TiXL4.2-alpha`, stable folders untouched.
- Stable install on the same machine → folders are `TiXL4.2`, alpha folders untouched.
- Logs land in the matching `Log\` subfolder for each.

---

## Phase 2: `VersionMarker` + version-aware welcome popup

**Goal:** The editor shows a welcome popup the first time a user runs a version they haven't run before — alpha or stable, fresh folder or version bump. Content adapts to build type and to whether the current folder has prior data.

### Tasks

1. **`VersionMarker` storage.** New `Core/Settings/VersionMarker.cs`:
   - Reads/writes `SettingsDirectory/versionMarker.json` containing one field: `lastRunVersion` (string, semver).
   - `LoadOrDefault()` returns the persisted version or `null` if the file is missing/corrupt.
   - `Update(Version current)` rewrites the file. Called once per launch after the popup is dismissed *or* immediately if no popup was shown.
   - Refuses to write a lower version than what's already on disk (downgrade case).

2. **Launch-time classification.** In `Program.cs` after `FileLocations` init, before the main loop. Produces one of:
   - `Silent` — `lastRunVersion == current` (steady state). Most launches.
   - `NewToUser` — `lastRunVersion` is missing or lower than current. **Fires the welcome popup** regardless of alpha vs stable. The popup itself checks whether the current folder has prior data (themes, layouts, keybindings, projects in the default folder) to decide whether to surface the import section.
   - `Downgrade` — `lastRunVersion` is higher than current. Log a warning, treat as `Silent` (don't overwrite the marker; user may go back).

3. **`WelcomeDialog`** under `Editor/Gui/Graph/Dialogs/`. One dialog class. Modal, styled to match `Help → About TiXL`. Two content variants and one conditional section:

   **Variant — Alpha** *(when `RuntimeAssemblies.IsAlpha`)*:
   - **Title.** "TiXL `<version>` alpha".
   - **Warning.** "This is a development build. Not for production work. Save often."
   - **Project planning link.** Button → opens `https://github.com/orgs/tixl3d/projects/3/views/8`.
   - **What's new.** Inline rendering of `release-notes/alpha.md` (rolling alpha changelog) if it exists; otherwise nothing.

   **Variant — Stable** *(when not alpha)*:
   - **Title.** "Welcome to TiXL `<version>`".
   - **What's new.** Inline rendering of `release-notes/<version>.md` if present; otherwise "see release notes on GitHub" link.

   **Common to both variants:**
   - **Your folders.** Resolved `SettingsDirectory` and `DefaultProjectFolder` with copy-to-clipboard. One paragraph explaining the per-version folder convention.
   - **Try the new features.** Link/button: "Open the Manual Features Tests window (sorted by recently added)".

   **Conditional section — Import from previous version.** Shown only when the current `SettingsDirectory` has no prior data (folder freshly created, no layouts/themes/keybindings written yet). Auto-detects the previous version's folders (see task 4). Granular checklist, all default off:
   - `[ ] Projects` — copied into current `DefaultProjectFolder`. Shows source-folder size.
   - `[ ] Settings` — `userSettings.json` allowlist.
   - `[ ] Layouts` — `Layouts/` subfolder.
   - `[ ] Themes` — `Themes/` subfolder.
   - `[ ] Keymaps` — `KeyBindings/` subfolder.
   Rows disabled with tooltip if the source is missing. "Choose other folder…" lets the user override the detected source.

   On a routine version bump within an already-populated folder (`lastRunVersion` present, just outdated), the import section is hidden — the popup is then just a "what's new" surface.

4. **Previous-version discovery.** Walk `Environment.SpecialFolder.ApplicationData` and `MyDocuments` for siblings of the current folder name matching the `TiXL<major>.<minor>[-suffix]` pattern. Pick the highest version that is *not* the current one. Prefer stable over alpha as the import source (`TiXL4.2` over `TiXL4.2-alpha` when running `4.3.0-alpha`). Surface the chosen source folder in the dialog so the user sees what's about to be copied.

5. **Import implementations.** All five categories run as a single "Import" action that processes whichever boxes are ticked. All operations are **copy** — sources are never modified.
   - **Projects** — recursive directory copy of the previous version's `DefaultProjectFolder` into the current one, skipping `bin/`, `obj/`, `.temp/`. Background task with progress shown in the dialog footer.
   - **Settings** — read previous `userSettings.json`, deserialize, copy only the allowlisted keys into the current settings, serialize. Allowlist lives in one named constant. Initial set: `KeyBindingName`, `UiScaleFactor`, recent-file list. (Themes/Layouts/Keymaps are deliberately separate categories below, not inside this allowlist.)
   - **Layouts** — copy contents of `<previous>/Layouts/` to `<current>/Layouts/`. Overwrite individual files; never delete user files in the current folder that aren't in the source.
   - **Themes** — copy contents of `<previous>/Themes/` to `<current>/Themes/`.
   - **Keymaps** — copy contents of `<previous>/KeyBindings/` to `<current>/KeyBindings/`.

6. **Help menu entry.** `Help → Welcome` reopens the dialog at any time, in both alpha and stable. Sits next to `Help → About TiXL` and uses the same styling.

### Risks

- **`userSettings.json` schema drift.** The curated allowlist contains the blast radius — drift only matters for the specific keys it touches.
- **Disk usage with "Projects".** Dialog shows source-folder size before the user ticks the box.
- **Layouts/Themes/Keymaps from an old version may reference files or styling that no longer exist.** The editor's existing missing-resource fallback handles this (gracefully degrades), but it's worth a manual-test pass to make sure a layout from 4.2 doesn't crash 4.3.
- **Previous-version discovery picks the wrong folder.** Mostly safe (newest non-current, stable preferred). The "Choose other folder…" escape hatch covers the rare ambiguous case.

### Manual test set

Add `.tests-manual/version-welcome-and-import.md` covering:
- **First alpha launch, no previous folders** → alpha popup shows, all import rows disabled with "no source found" tooltips. `lastRunVersion` written on dismiss.
- **First alpha launch, previous stable present** → alpha popup shows, import section visible, source folder shown, project size shown. Alpha warning + planning link visible.
- **Alpha bump within same folder** (4.2.0.3 → 4.2.0.4) → alpha popup shows, import section hidden (folder already populated), "what's new" rendered from `release-notes/alpha.md`.
- **First stable launch in new minor folder** (4.3.0 with 4.2 data present) → stable popup shows with import section. Alpha warning + planning link hidden.
- **Stable bump within same folder** (4.3.0 → 4.3.1) → stable popup shows, import section hidden, "what's new" rendered from `release-notes/4.3.1.md`.
- Tick only `Settings` → keymap name + recent files apply, layouts/themes untouched.
- Tick only `Projects` → project tree copied, source folder byte-identical afterwards.
- Tick `Layouts` + `Themes` → both subfolders populated, settings not touched.
- "Choose other folder…" → user can override detected source.
- Second launch at same version → no popup, `versionMarker.json` matches current.
- `Help → Welcome` → popup reopens with the same content variant.
- Downgrade (4.3.1 → 4.3.0) → no popup, `versionMarker.json` unchanged (still says 4.3.1).

---

## Phase 3: Release notes content pipeline

**Goal:** The welcome popup in Phase 2 has structured "what's new" content to render. This phase defines the on-disk format, the loader, and a fallback.

### Tasks

1. **`release-notes/<version>.md` format.** One file per stable release, hand-written at release-cut time:
   ```yaml
   ---
   version: 4.3.1
   date: 2026-06-14
   highlights:
     - "Faster shader compilation on cold start"
     - "Timeline now supports per-clip waveforms"
     - "Fixed crash on closing the parameter window with an active drag"
   ---
   ## What's new in 4.3.1
   <full markdown body>
   ```
   `highlights` renders inline in the popup; `body` is what "See full notes" opens.

2. **Rolling alpha file.** `release-notes/alpha.md` — one file updated as alpha work lands. No `version` field. The popup renders its `highlights` whenever alpha runs.

3. **`ReleaseNotesLoader`** — reads `release-notes/*.md` from editor resources at startup (shipped as content files). Exposes `TryGet(Version) → ReleaseNotesEntry?` and `TryGetAlpha() → ReleaseNotesEntry?`.

4. **Missing-file fallback.** If `release-notes/<version>.md` doesn't exist for the current stable, the popup shows "TiXL was updated to `<version>` — see release notes on GitHub" with a link. Don't block the upgrade UX on the release author having written notes.

### Risks

- **Release notes drift.** If the author forgets to add a file, every user sees the GitHub fallback. Acceptable. A CI check that the file exists for the version in `Tixl.props` would catch this — add later if the omission becomes a pattern.
- **Highlights are subjective.** Author picks 3–5 things that matter. Editorial, not exhaustive.

### Manual test set

Extends `.tests-manual/version-welcome-and-import.md`:
- Stable 4.3.0 → 4.3.1 with release notes file → popup shows highlights, "See full notes" works.
- Stable upgrade with no release notes file → popup shows GitHub fallback.
- Alpha launch with `release-notes/alpha.md` present → alpha popup renders rolling changelog.
- Downgrade → no popup, no marker change.

---

## Phase 4: Manual Features Tests recently-added sort

**Goal:** A tester opening the Manual Tests window from the welcome dialog sees newest tests first, so "try the new stuff" is obvious.

### Tasks

1. **Add `added: YYYY-MM-DD` to the frontmatter contract** in [`.tests-manual/README.md`](../.tests-manual/README.md). Required for new test sets, optional for legacy (legacy sort as oldest).

2. **Update existing test sets** with a best-guess `added` date (git introduction date is fine for the seed). One-time backfill commit.

3. **Update `Editor/Gui/Windows/TestRunner/TestSetParser.cs`** to read the new field.

4. **Add sort modes to the Manual Tests window:** `Recently added` (new default), `Alphabetical`, `By scope`. Default to `Recently added` when opened via the alpha welcome flow.

5. **Update `.agentic/AGENT_INSTRUCTIONS.md`** §"Documentation and Manual Tests" to mention the new `added` field requirement.

### Risks

- **Backfill accuracy doesn't matter much.** The seed dates are approximate; they exist only so older tests don't sort *above* genuinely new ones. Don't sink time into being precise.

### Manual test set

Extension to `.tests-manual/version-welcome-and-import.md` — "Open Manual Tests from welcome → newest sets at top, dates visible."

---

## Phase 5 (deferred): Agentic release-notes generation

Not part of v1. Sketching here so future work has a starting point.

- `release-notes/<version>.md` files (introduced in Phase 3) are the data contract; agentic generation just writes to them.
- A skill or CI job walks commit messages since the previous version's tag, groups them by area (operators, UI, audio…), and produces a draft `highlights` list + body. Author edits before merging.
- "Re-open welcome when new release notes land" — only if the user has opted in (don't surprise them with popups mid-session).
- This phase doesn't change anything the runtime sees — it's pure authoring tooling.

---

## Branch interaction note

This plan touches files that don't currently overlap with `feat/live-recording`:
- `Tixl.props`, `Core.csproj` — build plumbing
- `Core/Compilation/RuntimeAssemblies.cs`, `Core/Settings/FileLocations.cs`, `Core/Settings/VersionMarker.cs` (new) — Core
- `Editor/Program.cs` (small) — wiring
- `Editor/Gui/Graph/Dialogs/WelcomeDialog.cs`, `Editor/Gui/Help/ReleaseNotesLoader.cs` (both new)
- `release-notes/` (new top-level directory, hand-authored markdown)
- `.tests-manual/` (backfill + new sets) — additive
- `.agentic/AGENT_INSTRUCTIONS.md` (one paragraph)

Phase 1 is the cleanest atomic unit and can land first as its own PR. Phase 2 can land with a stub release-notes fallback (just the GitHub link); Phase 3 then drops in actual `release-notes/<version>.md` content without further code changes to the popup.
