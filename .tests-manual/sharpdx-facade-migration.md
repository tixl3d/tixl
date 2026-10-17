---
id: sharpdx-facade-migration
title: SharpDX to Graphics Facade Migration
scope: project-structure
tags: [projects, migration]
added: 2026-10-17
added-in-version: 4.4
prerequisites:
  - A user project in format V3 whose csproj still lists `SharpDX.*` using aliases and references (any project last saved by TiXL 4.3 or earlier).
  - One of its operators declares a slot with a fully qualified SharpDX type, e.g. `InputSlot<SharpDX.Direct3D11.TextureAddressMode>`.
related-help:
  - ../.help/docs/advanced/UpgradingToVulkan.md
---

Covers the format V3 -> V4 migration that moves a project from SharpDX onto the graphics facade:
the csproj's using aliases and assembly references, the operator sources, and the recompile that follows.

## Step: Loading a V3 project

**Action:**
Start the editor with the project in your projects folder and open it.

**Expected:**
- The log contains "Migrating ... to project format 4: Move operator code from SharpDX onto the graphics facade"
  and "Rewrote N source files ... onto the graphics facade".
- The log contains "Created backup before project format migration" with a `preFormat4` zip under the project's `.temp/Backup/`.
- The project compiles once and opens normally; operators evaluate.

## Step: Verifying the csproj

**Action:**
Open the project's `.csproj` in a text editor.

**Expected:**
- No `<Using Include="SharpDX...">` remains. The aliases point at `T3.Graphics.Compat.*`, `T3.Graphics.Format`,
  `T3.Graphics.ScissorRect` and `T3.Graphics.Viewport`, each named after its type.
- `Graphics.dll` and `Graphics.Compat.dll` are referenced; the `SharpDX.*.dll` references are gone.
- The file contains `<ProjectFormatVersion>4</ProjectFormatVersion>`.

## Step: Verifying the rewritten sources

**Action:**
Open an operator source that used a fully qualified SharpDX name, and one that never mentioned SharpDX.

**Expected:**
- `SharpDX.Direct3D11.TextureAddressMode` now reads `T3.Graphics.Compat.TextureAddressMode`;
  `SharpDX.Direct3D11.Texture2D` now reads `T3.Core.DataTypes.Texture2D`.
- The source without SharpDX references is untouched (its modification time did not change).

## Step: Duplicating an operator with a facade slot type

**Action:**
In the graph, right-click the operator with the `TextureAddressMode` input, choose `Duplicate as New Type...` and confirm the dialog.

**Expected:**
- No "Slot type error" dialog appears.
- The duplicate exists with the same inputs, and its address-mode parameter offers the wrap modes.

## Step: Migration runs only once

**Action:**
Restart the editor.

**Expected:**
- No further migration log lines for this project.

## Step: Sources that still need SharpDX

**Action:**
On a copy of a not-yet-migrated project, add `using SharpDX.WIC;` to one operator source, then start the editor.

**Expected:**
- The log warns that the project still references SharpDX in 1 file and names the file with "still uses SharpDX.WIC".
- The `SharpDX.*.dll` references stay in the csproj; everything else is migrated as above.
