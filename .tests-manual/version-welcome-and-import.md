---
id: version-welcome-and-import
title: Version welcome popup & import
scope: settings
tags: [user, essential]
added: 2026-05-31
added-in-version: 4.2
prerequisites:
  - A TiXL build to test (alpha or stable).
  - For import steps, a previous version's user folders present under `%APPDATA%` and `%USERPROFILE%\Documents` (e.g. a `TiXL4.2` folder while testing `4.3`).
related-help:
  - ../.agentic/Plans/Plan_AlphaSeparation.md
---

The first time you run a new version of TiXL, a [ui:WelcomeWindow|welcome popup] greets you. It can also offer to bring your projects, settings, layouts, themes, and keymaps over from a version you had installed before, so you don't start from scratch. This test walks through that welcome and import.

TiXL remembers that it has already greeted you for a given version. To see the first-run experience again, you'll need to start from a fresh settings folder (the prerequisites explain how) — otherwise the popup won't reappear.

## Step: First run shows the welcome popup

**Action:**
Starting from a fresh settings folder (so this version hasn't been run here before), start TiXL. Dismiss the user-name dialog if it appears (only on a brand-new install).

**Expected:**
- A floating welcome window appears.
- For an alpha build the title is "Welcome to the TiXL Alpha" and the sidebar shows Welcome, Import Settings, Import Projects, and Test new Features. The intro warns that this is a development build.
- For a stable build the title is "Welcome to TiXL" and the sidebar shows Welcome, Getting Started, and Projects — no import or feature-test pages, no alpha warning.

## Step: Import section appears only for a fresh folder (alpha only)

**Action:**
On an alpha build, look at the popup for an "Import from previous version" section.

**Expected:**
- On a freshly created settings folder with a previous version present on disk, the section is shown with checkboxes: Projects, Settings, Layouts, Themes, Keymaps.
- Categories with no matching source data are disabled, with a tooltip explaining why.
- The Projects row shows the approximate size of the previous project folder.
- If this version has been run in this folder before, the import section is absent.

## Step: Import settings only (alpha only)

**Action:**
Tick only `Settings`, then click "Import selected".

**Expected:**
- Your user name, UI scale, theme, and keymap from the previous version are applied (some take effect right away, some after a restart).
- Your layouts and saved themes are not brought over — only the settings.
- Your previous version is left untouched and still works as before.

## Step: Import projects (alpha only)

**Action:**
On a fresh run, tick only `Projects`, click "Import selected", and wait for completion.

**Expected:**
- While copying, the popup shows "Importing…".
- When done it shows an "Import complete" message.
- Your projects from the previous version now show up in this version.
- Your previous version still has all its projects — nothing was moved or deleted, only copied.

## Step: Release notes render with operator links

**Action:**
On the Welcome tab, scroll to the "Release Notes" section. Hover an operator reference such as `[SwiftCamDevice]`, then click it.

**Expected:**
- The release notes for this version render as nicely formatted text — headings, bullets, and clickable web links.
- Operator references like `[SwiftCamDevice]` show a hand cursor and, on hover, a tooltip describing that operator.
- Clicking an operator reference opens the Symbol Library filtered to that operator.
- An unknown operator name renders as plain text with no tooltip or click.
- If no release-notes file exists for the version, the section shows "No release notes for this version yet."

## Step: Open Feature Tests from the welcome (alpha only)

**Action:**
On an alpha build, on the Test new Features tab, select a set and click "Start Test".

**Expected:**
- The manual feature tests window becomes visible and starts the selected set.
- The welcome popup closes.

## Step: Getting Started links (stable only)

**Action:**
On a stable build, open the Getting Started page and click one of the tutorial links.

**Expected:**
- The page lists learning links (videos, tutorials, introduction, FAQ) and community links.
- Clicking a link opens it in the default browser; the window stays open.

## Step: Open a project from the welcome (stable only)

**Action:**
On a stable build with at least one project, open the Projects page and click a project.

**Expected:**
- The Projects page lists your projects with name and folder, like the project hub.
- Clicking a project loads it into the graph window and the welcome window closes.
- Clicking "New Project..." closes the welcome window and opens the new-project dialog.

## Step: Marker is written, popup does not repeat

**Action:**
Close the popup (Close button or click away), then restart TiXL without changing versions.

**Expected:**
- On restart at the same version, the welcome popup does not appear again — TiXL remembers it has already greeted you.

## Step: Reopen from the Help menu

**Action:**
Open `Help → Welcome`.

**Expected:**
- The welcome popup reopens with the same alpha/stable variant.
- On an already-used folder, no import section is shown.

## Step: Downgrade does not reset the marker

**Action:**
After running a newer build (e.g. `4.3.1`) in this folder, run an older build (e.g. `4.3.0`) against the same folder, then close it and start the newer build again.

**Expected:**
- No welcome popup appears for the older build — going back to an older version is not treated as a first run.
- When you return to the newer build, it still doesn't greet you — TiXL remembers the highest version you've run and doesn't forget it after a downgrade.
