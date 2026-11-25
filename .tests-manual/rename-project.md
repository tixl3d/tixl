---
id: rename-project
title: Rename Project
scope: hub
tags: [projects]
added: 2026-11-25
added-in-version: 4.4
prerequisites:
  - Two of your own projects exist, e.g. `Alpha` and `Beta` (create them with `Add Project...`).
  - `Alpha` contains at least one operator of its own besides its home operator.
---

Covers renaming a project from the Hub's Projects panel. The rename changes the project's folder,
`.csproj`, root namespace and home operator, and is applied while TiXL restarts.

## Step: Finding the project options

**Action:**
In the Hub's Projects panel, hover the `Alpha` row and click the small gear icon left of its thumbnail.

**Expected:**
- The icon shows the tooltip "Project options".
- The same context menu opens as with a right-click on the row, and the project is not opened.
- The menu offers `Rename Project...` below `Share Project...`.
- Built-in packages (Lib, Types, Examples) don't offer `Rename Project...`.

## Step: Rejecting invalid names

**Action:**
Choose `Rename Project...`, then type in turn: `beta`, `Beta`, `Al pha`, `Alpha.Two`.

**Expected:**
- The dialog opens with the current name `Alpha` and `Rename and Restart` disabled.
- `beta` is rejected because it doesn't start with a capital letter.
- `Beta` is rejected: a project with this name already exists.
- `Al pha` and `Alpha.Two` are rejected as invalid names.
- The muted line below the field shows the resulting namespace, e.g. `<user>.AlphaRenamed`.

## Step: Renaming

**Action:**
Make an unsaved change in `Alpha` (move an operator), then rename it to `AlphaRenamed` and click
`Rename and Restart`.

**Expected:**
- TiXL restarts on its own.
- The Projects panel lists `AlphaRenamed` with the namespace `<user>.AlphaRenamed`; `Alpha` is gone.
- On disk the folder is `AlphaRenamed/` with `AlphaRenamed.csproj` and `Symbols/AlphaRenamed.cs/.t3/.t3ui`.
- Opening the project shows the moved operator where you left it, and all its own operators work.
- The Symbol Library lists the project's operators under the new namespace.
- Projects that use operators of the renamed project still show them (they are referenced by id).

## Step: Undoing a rename

**Action:**
Right-click `AlphaRenamed`, open `Restore from Backup` and pick the newest entry marked "kept".

**Expected:**
- After the restore and restart the project is called `Alpha` again (namespace, `.csproj`, home operator).
- Its folder keeps the name `AlphaRenamed`, which is harmless.

## Step: Keeping a renamed home operator

**Action:**
In `Beta`, rename the home operator to `Workspace`. Then rename the project `Beta` to `Gamma`.

**Expected:**
- The project and its folder are renamed to `Gamma`.
- The home operator keeps the name `Workspace`.
