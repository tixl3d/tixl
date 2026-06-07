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

**2026-05-31** — Phase 2 landed:
- [`Core/Settings/VersionMarker.cs`](../Core/Settings/VersionMarker.cs) — persists `lastRunVersion` in `versionMarker.json`; `Classify()` returns `Silent` / `NewToUser` / `Downgrade`; `MarkCurrentVersionSeen()` refuses to lower the recorded version.
- [`Editor/Gui/Dialog/PreviousVersionImport.cs`](../Editor/Gui/Dialog/PreviousVersionImport.cs) — discovers the best sibling `TiXL<major>.<minor>[-suffix]` folder (highest non-current, stable preferred), and copies projects / settings-allowlist / layouts / themes / keymaps. All copy-only; sources never modified. "Folder already used" is keyed off the marker + `userSettings.json` (not theme/layout files, which startup may write).
- [`Editor/Gui/Dialog/WelcomeDialog.cs`](../Editor/Gui/Dialog/WelcomeDialog.cs) — one modal, alpha/stable content variants, conditional import section (only on a fresh folder). Project copy runs on a background task; small categories inline. Stamps the marker on close.
- Wired into [`T3Ui`](../Editor/Gui/T3Ui.Update.cs): one-shot `CheckForVersionWelcome()` fires after layout is ready and any startup popup (user-name) has closed. `Help → Welcome` reopens it.
- Manual test set: [`.tests-manual/version-welcome-and-import.md`](../.tests-manual/version-welcome-and-import.md).
- Editor builds clean (only the pre-existing `ProjectSettingsWindow` CS8604 warning, unrelated).

Deferred to Phase 3: the "What's new" section currently just links to GitHub Releases; the per-version `release-notes/<version>.md` rendering replaces that link without touching the popup's structure.

**2026-05-31 (Window integration)** — `WelcomeAlphaWindow` now inherits the editor's `Window` base and is registered in `WindowManager` (skipped in the Windows menu; opened via `Open()` from the version-welcome trigger and `Help → Welcome`). This gives it the same chrome/background as the Settings window for free and removes the bespoke `ImGui.Begin`/visibility/marker handling (now `Config.Visible` + the base's `Close()` override stamps the marker). `WindowPaddingOverride = Vector2.Zero` keeps the sidebar flush. (Trade-off: it inherits the base's default 550×450 first-open size instead of the old centered 680×480 — revisit if the base grows a size hook.)

**2026-05-31 (redesign)** — Per design feedback, the single-column modal was replaced with a **non-modal, Settings-style tabbed window** (sidebar: Welcome / Import Settings / Import Projects / Test new Features), reusing the `SettingsWindow` child-layout pattern:
- [`WelcomeDialog.cs`](../Editor/Gui/Dialog/WelcomeDialog.cs) is now a standalone floating `ImGui.Begin` window (no longer `ModalDialog`). Sidebar items show a checkmark once their import has run.
- **Import Settings** tab: granular category checklist — Editor Settings / Themes / Keyboard Maps / Layouts — each mapping to the corresponding `PreviousVersionImport` op. (This restores granularity over the two-button sketch, per confirmation.)
- **Import Projects** tab: per-project list (`PreviousVersionImport.EnumerateProjects`), already-imported rows disabled + labelled, open-source-folder icon per row, background copy.
- **Test new Features** tab: lists `.tests-manual` sets via `TestSetParser.LoadAll`, single-select rows (tag pills + step count), "Start Test" → `ManualTestRunnerWindow.StartSet(id)` (new entry point) and closes the welcome.
- **User-name dialog deferral**: `CheckForVersionWelcome()` runs before the user-name prompt; the prompt is gated on `_versionWelcomeChecked && !WelcomeDialog.IsVisible`, so on a fresh install the welcome shows first and the name prompt follows after it closes.
- Editor builds clean (only the pre-existing `ProjectSettingsWindow` CS8604 warning).

Release Notes (Welcome tab) is still a stub; Phase 3 fills it in — including the operator-reference link enhancement now recorded in Phase 3 task 5.

**2026-06-07 (manual folder override)** — Added a developer/power-user escape hatch on top of the version-derived folder naming, so two builds of the *same* version (e.g. two parallel dev checkouts) can keep separate settings/projects. [`FileLocations`](../Core/Settings/FileLocations.cs) reads `TIXL_OVERRIDE_VERSION_ID` during static init; when set, it replaces the prerelease suffix in `VersionedAppFolderName` (`TiXL4.2-skillQuest` instead of `TiXL4.2-alpha`). The value is sanitised against `Path.GetInvalidFileNameChars()` so it can't redirect the tree outside AppData. The Editor also accepts `--override-version-id=<id>` ([`Program.ApplyVersionIdOverrideArg`](../Editor/Program.cs)), which just seeds the same env var before any `FileLocations` access and logs the resolved folder. Player inherits the env-var path for free (no CLI arg). Manual test steps appended to [`alpha-folder-separation.md`](../.tests-manual/alpha-folder-separation.md).

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

**Progress (2026-05-31):** Core of the phase landed. Release notes live under `.help/release-notes/` (`alpha.md` rolling for alpha builds, `<major>.<minor>.md` for stable). [`ReleaseNotesLoader`](../Editor/Gui/Help/ReleaseNotesLoader.cs) resolves the dir the same way `TestSetParser` does and returns the markdown; the Welcome tab renders it through `MarkdownView` (so `[text](url)` links work) and falls back to "No release notes for this version yet." when the file is missing. Operator-reference links (task 5) are wired: [`MarkdownOperatorLinks`](../Editor/Gui/Styling/Markdown/MarkdownOperatorLinks.cs) resolves `[OpName]` → symbol via a cached name lookup over `EditorSymbolPackage.AllSymbols`, shows a namespace+description hover tooltip, and on click calls `SymbolLibrary.Reveal(symbolId)` (exposed via `WindowManager.SymbolLibrary`). `Reveal` reuses the library's existing tree-expand mechanism (`_expandToSymbolTargetId`) and a new `_scrollToSymbolId` that scrolls the operator into view — it does **not** touch the search filter. Op-ref fragments are colored by the operator's output **type color** via a new `MarkdownView.OperatorRefColor` resolver (`MarkdownOperatorLinks.GetOperatorColor`; unknown names fall back to the plain body color so they don't look like live links). The hover tooltip renders the operator's `Description` through the markdown renderer too (code spans, lists, and nested `[OpName]` refs read correctly; nested refs pass `suppressTooltip` to avoid stacking tooltips). Still open: the per-version `highlights` frontmatter + "See full notes" split, and the `suppressTooltip` path for when these render inside the test-runner's hover tooltip. *(Both deferred — out of scope per user.)*

**Packaging gap resolved (2026-05-31):** release notes are one file per minor, `release-notes/v<major>.<minor>.md`, shared by alpha and stable (`ReleaseNotesLoader` no longer branches on `IsAlpha`). A new [`ShippedContent`](../Editor/Gui/Help/ShippedContent.cs) resolves shipped markdown folders — repo source in a dev checkout (live files for the test-runner reload), the copy next to the binaries in a packaged release. `ReleaseNotesLoader`, `TestSetParser`, and `EmbeddedHelpLoader` all route through it, and `Editor.csproj` now copies `.help/release-notes`, `.tests-manual`, and `.help/embedded` into the output. So release notes and guided tests are no longer invisible in a packaged alpha build.

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

5. **Operator-reference links in the markdown renderer.** The renderer already recognises bare `[OpName]` fragments (no `(url)` suffix) and exposes an `onOperatorRef(opName)` callback ([`MarkdownView`](../Editor/Gui/Styling/Markdown/MarkdownView.cs)). This task supplies the callback:
   - **Resolve `OpName` → `SymbolId`** via the symbol registry (name lookup over `SymbolUiRegistry` / `SymbolRegistry`). Cache the lookup.
   - **Hover tooltip** built from the resolved `SymbolUi`: type color, namespace, description — the same affordance the Symbol Library shows. Guard against recursion: skip the tooltip when the markdown is *already* being rendered inside a tooltip (e.g. the test-runner row hover), since ImGui can't nest tooltips cleanly.
   - **Click → reveal in Symbol Library**: open the library and focus/select that symbol.
   - Makes release notes ("`[DrawLines]` now fades long lines") and `.tests-manual` intros navigable. The Welcome tab's Release Notes area renders through the same renderer, so it inherits this for free.
   - Unresolved names (renamed/removed operators) render as plain text, not a dead link.

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

## Phase 4: Manual Features Tests — recency sort + run history

**Goal:** A tester opening the Feature Tests sees the newest tests first, and sets they've already completed are marked done — so "try the new stuff" and "what's left" are both obvious. The same recency metadata feeds the Welcome tab's Test new Features list.

**Landed (2026-05-31):** `TestSet` gained `Added` (date) + `AddedInVersion`, parsed from `added:` / `added-in-version:` frontmatter ([`TestSetParser`](../Editor/Gui/Windows/TestRunner/TestSetParser.cs)). All 11 existing sets were backfilled from git first-commit dates (all `4.2`). `TestSetParser.Sort` + a "Recently added / Alphabetical / By scope" dropdown in the runner; `LoadAll` defaults to recency, so the Welcome list inherits it. Run completion is persisted to `testRunHistory.json` ([`TestRunHistoryStore`](../Editor/Gui/Windows/TestRunner/TestRunHistoryStore.cs)) when every step of a set gets an outcome; completed sets show a checkmark (blue=passed, accent=had-issues) in a left gutter of both the runner list and the Welcome tab list. **Full results** are serialized by [`TestRunExport`](../Editor/Gui/Windows/TestRunner/TestRunExport.cs): every finished run auto-saves to `TestRuns/<timestamp>.json` (per-step outcome+comment+timestamp, plus a per-set pass/fail/other/skipped/pending tally for future progress bars), and the Summary screen's "Copy JSON" / "Open Saved Runs" buttons are wired. README + AGENT_INSTRUCTIONS document the new frontmatter requirement.

### Tasks

1. **Frontmatter: `added` date + `addedInVersion`.** Add both to the `.tests-manual` contract in [`.tests-manual/README.md`](../.tests-manual/README.md):
   - `added: YYYY-MM-DD` — when the set was first added.
   - `addedInVersion: 4.2` — the TiXL `major.minor` it first shipped in.
   Required for new sets; optional for legacy (sort as oldest).

2. **Backfill from git history.** Compute each existing set's first-commit date and map it to the TiXL version active at that time, then write `added` + `addedInVersion` into every `.tests-manual/*.md`. Drive from `git log --diff-filter=A --follow --format=%ad -- <file>` for the introduction date; map date → version from the release history. One-time backfill (line-ending-safe per the bulk-edit rules in AGENT_INSTRUCTIONS).

3. **Parse the new fields** in [`TestSetParser.cs`](../Editor/Gui/Windows/TestRunner/TestSetParser.cs); surface on `TestSet`.

4. **Sort modes** in the Feature Tests window and the Welcome tab list: `Recently added` (default), `Alphabetical`, `By scope`.

5. **Persist run results to AppData.** Save per-set completion (last run date, outcome summary) to a `testRunHistory.json` in `SettingsDirectory`. The runner's `RunReport` already has per-step outcomes; collapse to a per-set status on finish.
   - Mark completed sets with a checkmark in both the runner list and the Welcome tab's Test new Features list (the row renderer + `NavigationSidebar.Item` already support a trailing checkmark).
   - "Completed" = all steps recorded pass/other (define precisely during implementation).

6. **Update AGENT_INSTRUCTIONS** §"Documentation and Manual Tests" to require `added` + `addedInVersion` on new sets.

### Risks

- **Backfill accuracy is approximate** — seed dates just need to keep genuinely-new sets above old ones. Don't over-invest.
- **Run-history schema drift** — keep `testRunHistory.json` minimal (set id → {lastRunUtc, status}) so it survives test-set edits; ignore unknown/removed ids.

### Manual test set

Extends `.tests-manual/version-welcome-and-import.md` — newest sets sort to the top; a completed set shows a checkmark on next open.

---

## Phase 5: `AddedInVersion` metadata + new-operator highlighting

**Goal:** Operators added or changed in the current version are discoverable in the Symbol Library (e.g. a small blue dot), gated behind a user option. This establishes the `AddedInVersion` concept that Phase 6 builds on.

### Tasks

1. **`AddedInVersion` on `SymbolUi`.** A `major.minor` string (or empty) stored alongside the symbol's UI metadata. Authored when an operator is introduced; persisted in the `.t3ui`/symbol metadata.

2. **New-operator highlight in the Symbol Library.** When `AddedInVersion` equals the running version (or is within N versions), draw a small accent dot on the entry. Threshold-cull at low zoom like other Symbol Library detail.

3. **Symbol Library settings popup (new).** There's no settings popup for the Symbol Library yet — add one, visually aligned with the existing **Asset Library** settings icon + popup. First option: "Highlight new operators" (on/off, and maybe "since version"). Reuse the Asset-Lib popup pattern so the two libraries stay consistent.

### Risks

- **Authoring burden.** `AddedInVersion` is only useful if it's filled in. Consider deriving a default from git on first save, or a lint that nudges when a new symbol lacks it. Decide during implementation.
- **Metadata migration.** Existing symbols have no `AddedInVersion`; treat missing as "unknown / not new", never as "new".

---

## Phase 6: Feature cross-reference registry (design-first)

**Goal:** A single registry of "features" that ties **release notes ↔ a feature entry (added in version) ↔ the actual UI component**, so users can discover new/improved features directly in the interface, and release notes can deep-link to them.

**This phase needs a design pass before any code** — it touches menus app-wide and overlaps Phase 5's `AddedInVersion`. Sketch only:

- **Feature entry**: id, title, `addedInVersion`, optional description, optional link to a release-note section.
- **`FeatureMenuItem`**: a drop-in replacement for plain `MenuItem`s that carries a feature reference. Lets the UI flag new/changed menu actions (e.g. a dot or "new" badge) and lets release notes point at a concrete menu path.
- **"Highlight new features" user option**: when on, `FeatureMenuItem`s whose `addedInVersion` matches the current version get an accent.
- **Cross-reference both ways**: release-notes markdown can reference a feature id (rendered like the `[OpName]` links from Phase 3); the UI component can link back to its release-note entry.
- **Shared `addedInVersion` source**: Phases 4 (tests), 5 (operators), and 6 (features) all express "added in version X" — design the metadata once so all three read from a common shape rather than three parallel mechanisms.

Open questions for the design pass: where the feature registry lives (static table vs attributes vs data file), how `FeatureMenuItem` avoids per-frame allocations in the menu bar, and whether feature ids are authored by hand or generated.

**Building block landed (2026-05-31):** a reusable window-level help affordance, independent of the Parameter window (which is untouched):
- [`EmbeddedHelpLoader`](../Editor/Gui/Help/EmbeddedHelpLoader.cs) reads `.help/embedded/<id>.md`. The folder is copied next to the binaries by an `Editor.csproj` target (like `EditorResources`), so it resolves in both dev and packaged builds — solving the release-packaging gap that still affects `ReleaseNotesLoader`/`TestSetParser` (those should migrate to this shipped-folder approach too).
- [`DocumentationButton`](../Editor/Gui/Help/DocumentationButton.cs) — a `HelpOutline` icon that renders the embedded markdown as a tooltip (with the type-colored `[OpName]` links) on hover and opens the wiki page on click. First instance is on the Guided Feature Tests header; `[GuidedFeatureTests.md](../.help/embedded/GuidedFeatureTests.md)` is the seed doc.
- This is the natural seed for the feature ↔ doc ↔ UI cross-reference: a window/feature declares `(docId, wikiUrl)` and gets discoverable in-editor docs.

---

## Phase 7 (deferred): Agentic release-notes generation

Not part of v1. Sketching here so future work has a starting point.

- `release-notes/<version>.md` files (introduced in Phase 3) are the data contract; agentic generation just writes to them.
- A skill or CI job walks commit messages since the previous version's tag, groups them by area (operators, UI, audio…), and produces a draft `highlights` list + body. Author edits before merging.
- "Re-open welcome when new release notes land" — only if the user has opted in (don't surprise them with popups mid-session).
- This phase doesn't change anything the runtime sees — it's pure authoring tooling.

---

## Branch interaction note

Key files by area:
- **Phase 1 (landed):** `Tixl.props`, `Core.csproj` (build plumbing); `Core/Compilation/RuntimeAssemblies.cs`, `Core/Settings/FileLocations.cs`.
- **Phase 2 (landed):** `Editor/Gui/Dialog/VersionMarker.cs`, `WelcomeAlphaWindow.cs`, `PreviousVersionImport.cs` (all Editor — version marker lives in `Editor/`, not `Core/`); `Editor/Gui/Styling/NavigationSidebar.cs` (shared with Settings); wiring in `T3Ui`, `AppMenuBar`, `SettingsWindow`, `ManualTestRunnerWindow`.
- **Phase 3+:** `Editor/Gui/Help/ReleaseNotesLoader.cs` (new), `release-notes/` (new top-level dir, hand-authored markdown), `MarkdownView` op-ref callback.
- **Cross-cutting:** `.tests-manual/` (frontmatter + backfill), `SymbolUi` (`AddedInVersion`), `.agentic/AGENT_INSTRUCTIONS.md`.

`WelcomeAlphaWindow` is named to leave room for a future general-purpose `WelcomeWindow` (the lighter "what's new in this stable version" surface) — see the Help-menu note in Phase 2.
