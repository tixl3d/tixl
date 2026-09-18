---
id: build-output-to-temp-migration
title: Build Output to .temp Migration
scope: project-structure
tags: [projects, migration]
added: 2026-08-26
added-in-version: 4.3
prerequisites:
  - A user project in format V2 (Symbols/ folder present, build output still in root bin/obj) from before this change, or any project committed earlier today.
---

Covers the format V2 -> V3 migration that moves build output (`bin/`, `obj/`) under `.temp/`
via a generated `Directory.Build.props`, leaving the project root with content only.

## Step: Loading a V2 project

**Action:**
Start the editor with the project in your projects folder and open it.

**Expected:**
- The log contains "Migrating ... to project format 3: Move build output under .temp".
- The project compiles once (fresh output location) and opens normally; operators evaluate.

## Step: Verifying the folder layout

**Action:**
Right-click the project and choose `Reveal in Explorer`.

**Expected:**
- The root contains only: the `.csproj`, `Directory.Build.props`, `Symbols/`, `Assets/`,
  `.meta/`, `dependencies/` (if used) and `.temp/`.
- `bin/` and `obj/` are gone from the root; `.temp/` contains `bin/`, `obj/` and `Backup/`.
- No new root `obj/` reappears after further builds (NuGet artifacts live in `.temp/obj/`).

## Step: Hot reload and saving

**Action:**
Edit an operator's C# code (or create a new operator) and let the project recompile; save.

**Expected:**
- Hot reload works; the new build lands under `.temp/bin/Debug/`.
- No spurious "Code file changed" log lines for files under `.temp/`.

## Step: Player export

**Action:**
Export an operator with `Export Executable`.

**Expected:**
- The export succeeds; the exported player runs. (The Release build output is read from
  `.temp/bin/Release/` internally.)

## Step: Migration runs only once

**Action:**
Restart the editor.

**Expected:**
- No further migration log lines; the csproj contains `<ProjectFormatVersion>3</ProjectFormatVersion>`.

## Step: Share export carries the props file

**Action:**
Run `Share Project...` on the migrated project and inspect the resulting `.nupkg` as a zip.

**Expected:**
- The archive root contains `Directory.Build.props` next to the `.csproj`, so a hand-unpacked
  copy builds into `.temp/` without editor intervention.
