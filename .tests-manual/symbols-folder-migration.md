---
id: symbols-folder-migration
title: Symbols Folder Migration
scope: project-structure
tags: [projects, migration]
added: 2026-08-26
added-in-version: 4.3
prerequisites:
  - A user project saved with an older TiXL version, whose operator files still live in namespace folders at the project root (no Symbols/ folder yet).
---

Covers the silent one-time migration that moves a project's operator files
(`.t3`, `.t3ui` and their C# sources) into the `Symbols/` folder. Symbol
discovery only looks there; helper C# files without a symbol stay where they are.

## Step: Loading a legacy project

**Action:**
Start the editor with the legacy project in your projects folder, then open it
from the Projects panel.

**Expected:**
- The project loads and renders as before; no operators are missing.
- The log contains "Migrated project ... to the Symbols folder structure" and a
  "Created backup before project structure migration" line.

## Step: Verifying the folder layout

**Action:**
Right-click the project and choose `Reveal in Explorer`.

**Expected:**
- A `Symbols/` folder contains the former root-level namespace folders and the
  home symbol's `.cs`/`.t3`/`.t3ui` trio.
- The emptied namespace folders are gone from the project root.
- `Assets/`, `dependencies/` and `.meta/` are untouched.
- Helper `.cs` files that are not operators (no symbol next to them) remain at
  their old location.

## Step: Checking the backup

**Action:**
Right-click the project and open `Restore from Backup`.

**Expected:**
- A pinned backup marked "pre-format-upgrade" (keep tag `preSymbolsFolder`)
  exists from just before the migration.

## Step: Migration runs only once

**Action:**
Restart the editor.

**Expected:**
- The project loads normally with no further migration log lines.
- The csproj contains `<ProjectFormatVersion>2</ProjectFormatVersion>` and
  its Release content includes start with `Symbols/`.

## Step: Editing after migration

**Action:**
Create a new operator in the project, rename its namespace, and save.

**Expected:**
- The new symbol's files are created under `Symbols/<namespace>/`.
- The rename moves the files to the matching subfolder inside `Symbols/`.
- Hot reload and compilation work as before.
