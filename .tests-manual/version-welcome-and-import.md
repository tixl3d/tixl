---
id: version-welcome-and-import
title: Version welcome popup & import
scope: settings
tags: [essential]
added: 2026-05-31
prerequisites:
  - A TiXL build to test (alpha or stable).
  - For import steps, a previous version's user folders present under `%APPDATA%` and `%USERPROFILE%\Documents` (e.g. a `TiXL4.2` folder while testing `4.3`).
related-help:
  - ../.agentic/Plans/Plan_AlphaSeparation.md
---

Verifies the welcome popup that appears the first time a user runs a version they haven't run in this settings folder before, and the optional import of projects/settings/layouts/themes/keymaps from a previously installed version.

The "first time" state is tracked by `versionMarker.json` in the settings folder. To re-test the first-run experience, close TiXL and delete that file (and, to test the fresh-folder import section, start from a settings folder with no `userSettings.json`).

## Step: First run shows the welcome popup

**Action:**
With no `versionMarker.json` in the current settings folder, start TiXL. Dismiss the user-name dialog if it appears (first-ever runs only).

**Expected:**
- A modal welcome popup appears.
- For an alpha build the title references the alpha and a "development build" warning plus an "Open project planning board" button are shown.
- For a stable build the title is a plain welcome with no alpha warning.
- The Settings folder and Projects folder paths are shown, each with a "Copy" button.

## Step: Import section appears only for a fresh folder

**Action:**
Look at the popup for an "Import from previous version" section.

**Expected:**
- On a freshly created settings folder with a previous version present on disk, the section is shown with checkboxes: Projects, Settings, Layouts, Themes, Keymaps.
- Categories with no matching source data are disabled, with a tooltip explaining why.
- The Projects row shows the approximate size of the previous project folder.
- If the current folder was already used before (it has a `versionMarker.json` or `userSettings.json`), the import section is absent.

## Step: Import settings only

**Action:**
Tick only `Settings`, then click "Import selected".

**Expected:**
- The user name, UI scale, theme name, and keymap name from the previous version are applied (visible after the import, some after restart).
- Layouts and themes files are not copied.
- The previous version's folders are unchanged.

## Step: Import projects

**Action:**
On a fresh run, tick only `Projects`, click "Import selected", and wait for completion.

**Expected:**
- While copying, the popup shows "Importing…".
- When done it shows an "Import complete" message.
- The project tree appears under the current version's Documents folder.
- The previous version's project folder is byte-for-byte unchanged (nothing moved or deleted).

## Step: Release notes render with operator links

**Action:**
On the Welcome tab, scroll to the "Release Notes" section. Hover an operator reference such as `[SwiftCamDevice]`, then click it.

**Expected:**
- The release notes from `.help/release-notes/alpha.md` (alpha) or `<major>.<minor>.md` (stable) render as formatted markdown — headings, bullets, and inline `[text](url)` links.
- Operator references like `[SwiftCamDevice]` show a hand cursor and a tooltip with the operator's namespace and description on hover.
- Clicking an operator reference opens the Symbol Library filtered to that operator.
- An unknown operator name renders as plain text with no tooltip or click.
- If no release-notes file exists for the version, the section shows "No release notes for this version yet."

## Step: Open Feature Tests from the welcome

**Action:**
On the Test new Features tab, select a set and click "Start Test".

**Expected:**
- The manual feature tests window becomes visible and starts the selected set.
- The welcome popup closes.

## Step: Marker is written, popup does not repeat

**Action:**
Close the popup (Close button or click away), then restart TiXL without changing versions.

**Expected:**
- `versionMarker.json` exists in the settings folder and records the current version.
- On restart at the same version, the welcome popup does not appear.

## Step: Reopen from the Help menu

**Action:**
Open `Help → Welcome`.

**Expected:**
- The welcome popup reopens with the same alpha/stable variant.
- On an already-used folder, no import section is shown.

## Step: Downgrade does not reset the marker

**Action:**
With the marker recording a higher version (e.g. `4.3.1`), run an older build (e.g. `4.3.0`) against the same folder, then close it and inspect `versionMarker.json`.

**Expected:**
- No welcome popup appears for the older build (it's classified as a downgrade).
- `versionMarker.json` still records the higher version — it is not lowered.
