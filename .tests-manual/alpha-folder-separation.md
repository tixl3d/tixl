---
id: alpha-folder-separation
title: Alpha & stable folder separation
scope: settings
tags: [dev, essential]
added: 2026-05-31
added-in-version: 4.2
prerequisites:
  - Two TiXL builds available: one stable, one alpha (or two consecutive alpha builds where one was built before this change landed).
  - File Explorer (or any file manager) open at `%APPDATA%` and at `%USERPROFILE%\Documents`.
related-help:
  - ../.agentic/Plans/Plan_AlphaSeparation.md
---

Verifies that an alpha build writes its settings, themes, layouts, keybindings, logs, and default project folder under a `-alpha`-suffixed sibling of the stable build's folder — so installing an alpha next to a stable cannot clobber the stable's data.

The currently-running build's alpha/stable status is the value of `RuntimeAssemblies.IsAlpha`, derived from `TixlVersionSuffix` in `Tixl.props` at compile time.

## Step: Identify the version being tested

**Action:**
Start the TiXL build under test. In the splash log line (and in `Help → About TiXL`), note the version string.

**Expected:**
- A version like `v4.2.0.2-alpha` indicates an alpha build.
- A version like `v4.2.0.2` (no suffix) indicates a stable build.

## Step: Verify the per-version settings folder

**Action:**
Open `%APPDATA%` in File Explorer.

**Expected:**
- For an alpha build there is a folder named `TiXL<major>.<minor>-alpha` (e.g. `TiXL4.2-alpha`).
- For a stable build there is a folder named `TiXL<major>.<minor>` (e.g. `TiXL4.2`).
- The folder for the *other* build type does not appear because of this run. (It may already exist from a previous install — that's fine, just confirm this run didn't create it.)

## Step: Verify the per-version Documents folder

**Action:**
Open `%USERPROFILE%\Documents` in File Explorer.

**Expected:**
- A folder with the same name as the settings folder above (`TiXL4.2-alpha` for alpha, `TiXL4.2` for stable).
- The "other" version's Documents folder is untouched by this run.

## Step: Verify logs land in the matching folder

**Action:**
Inside the settings folder from the previous step, open `Log\` and confirm a new `.log` file was created with a timestamp matching this session.

**Expected:**
- The active log lives under the matching `TiXL<version>[-alpha]\Log\` folder.
- No log file was created in the *other* build type's folder during this session.

## Step: Cross-check both builds side by side (if both available)

**Action:**
Close the current build, start the other build (alpha if you just ran stable, or vice versa), then close it.

**Expected:**
- Each build writes only to its own `TiXL<version>[-alpha]\` folder.
- Settings, themes, layouts, and keybindings touched in one build are not visible in the other (they live in separate folders).
- Both folders' logs grow only when their matching build runs.

## Step: Override the folder suffix with an environment variable

Lets a single build run as a second, isolated session — useful for two parallel dev checkouts of the same version.

**Action:**
Set the environment variable `TIXL_OVERRIDE_VERSION_ID=skillQuest` (in the run configuration, or `setx`/a shell `$env:`) and start the build. Watch the startup log lines.

**Expected:**
- The log shows a `Settings folder overridden via 'TIXL_OVERRIDE_VERSION_ID': …\TiXL<version>-skillQuest` line.
- `%APPDATA%\TiXL<version>-skillQuest\` and `%USERPROFILE%\Documents\TiXL<version>-skillQuest\` are created; the normal `TiXL<version>[-alpha]\` folders are untouched by this run.
- The suffix replaces the build's own suffix — an alpha build run with the override lands in `TiXL<version>-skillQuest`, not `TiXL<version>-alpha-skillQuest`.

## Step: Override via the command-line argument

**Action:**
Clear the environment variable, then launch the editor with `--override-version-id=parallelB` (e.g. as a Rider run-configuration program argument, or `Tooll3.exe --override-version-id=parallelB`).

**Expected:**
- Folders resolve to `TiXL<version>-parallelB` under both `%APPDATA%` and Documents, with the same override log line.
- Running one session with `TIXL_OVERRIDE_VERSION_ID=skillQuest` and a second with `--override-version-id=parallelB` keeps the two sessions' settings, layouts, and projects fully separate.
