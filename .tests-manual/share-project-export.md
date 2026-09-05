---
id: share-project-export
title: Share Project Export
scope: hub
tags: [projects, sharing]
added: 2026-08-26
added-in-version: 4.2
prerequisites:
  - At least one of your own projects exists (not a built-in package like Lib or Examples).
  - The project uses a few Lib operators and at least one asset file (e.g. an image loaded by [LoadImage]).
---

Covers exporting a project as a shareable package file (`.nupkg`) from the
Projects panel, including the two opt-in size reductions and the resulting
archive's contents.

## Step: Opening the share dialog

**Action:**
In the Hub's Projects panel, right-click one of your own projects and choose
`Share Project...`.

**Expected:**
- The "Share project" dialog opens, showing the project name.
- A folder field is prefilled (defaults to the Desktop).
- Built-in packages (Lib, Types, Examples) do not offer `Share Project...` in
  their context menu.

## Step: Checking the opt-in reduction toggles

**Action:**
Look at the checkboxes below the folder field.

**Expected:**
- If the project contains operators not reachable from its home canvas, a
  "Tree shake unused operators (N symbols)" checkbox is shown with a plausible count.
- If the project's Assets folder contains files no operator references, an
  "Exclude unreferenced assets (N files, size)" checkbox is shown.
- Both are off by default. Zero-gain toggles are not shown at all.

## Step: Exporting the package

**Action:**
Leave both toggles off and click `Export`.

**Expected:**
- A success message shows the full path of the written `.nupkg` file.
- The destination folder opens in Explorer and contains
  `<RootNamespace>.<version>.nupkg`.

## Step: Verifying the archive content

**Action:**
Rename a copy of the `.nupkg` to `.zip` and open it.

**Expected:**
- It contains the project's files in their normal folder layout (`.csproj`,
  `.cs`, `.t3`, `.t3ui`, `Assets/...`), plus a `<name>.nuspec` manifest.
- `bin`, `obj`, `.git`, `.temp` (backups) and `Export` folders are not included.
- Generated sidecar files (`*.proxy.mov` video proxies, `*.waveform.png`) are
  not included, even inside referenced asset folders.
- The nuspec lists the built-in packages the project actually uses (e.g. `Lib`)
  as dependencies.

## Step: Round-trip into a clean project folder

**Action:**
Unzip the archive into a new folder inside your TiXL projects directory
(folder name = project name), delete the `.nuspec`, `_rels` and
`[Content_Types].xml` entries, then restart the editor.
(For a stricter test, use a second TiXL install or a different
`TIXL_OVERRIDE_VERSION_ID`, and remove or rename the original project first.)

**Expected:**
- The editor compiles and lists the project; it opens and renders as before.
- Shaders and assets resolve without errors in the console.

## Step: Cross-project reference guard

**Action:**
In a project that uses an operator from *another* of your own projects, open
`Share Project...`.

**Expected:**
- The dialog shows a red note listing which operators reference the other
  project.
- The `Export` button is disabled.
