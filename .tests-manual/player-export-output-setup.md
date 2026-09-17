---
id: player-export-output-setup
title: Export ships the output setup to the player
scope: export
tags: [player, export, projection-mapping]
added: 2026-09-18
added-in-version: 4.3
prerequisites:
  - A writable project is open whose editor build is Release, so `Player/` exists next to the editor binaries.
  - The project has an output setup with at least one output and one surface (the Output Setup button on an output window's toolbar creates one on first use).
related-help:
  - ../.help/docs/using/ExportExecutables.md
  - ../.help/docs/using/OutputSetup.md
---

Covers the setup travelling with an exported executable: the `*.setup.json` files are copied
beside it, the player loads one at startup, and operators that read the venue work there.
The local bindings stay behind by design.

## Step: The setup lands in the export

**Action:**
1. Note the setup's name in the output window's strip header.
2. Right-click the project op in the graph and choose **Export as Executable...**, then confirm.
3. Open the export folder (`Export/<op name>/` inside the project folder).

**Expected:**
- A `.meta` folder sits next to the renamed `.exe`, holding one `<name>.setup.json` per setup in
  the project — the same files as the project's own `.meta` folder.
- No `outputs.machine.json` is present: the bindings name this computer's displays and are not
  exported.
- The editor's console reports how many output setups were exported.

## Step: The player loads it

**Action:**
Start the exported executable and, once it is running, open its log (the `.temp/` folder next to
the executable, or the console window with **Show Log Messages** enabled).

**Expected:**
- A line reports the loaded setup by name with its output and surface counts.
- The player shows the setup's first output composited — patches warped into place, surfaces
  corner-pinned — rather than a raw texture output.

## Step: Venue-reading operators work in the export

**Action:**
1. In the project, add a **StageGeometry** → **GeometryToMesh** → **DrawMesh** chain so the room's
   surfaces are visible in the output, and export again.
2. Start the exported executable.

**Expected:**
- The room's surfaces render in the player exactly as they do in the editor's output window.
  Before the setup was exported this chain produced nothing, because StageGeometry had no
  active setup to read.

## Step: Installation mode ships the bindings and skips the dialog

**Prerequisite:** at least one output is bound to a display in LOCAL BINDINGS.

**Action:**
1. In the project's `Executable` settings set **Player Mode** to **Installation** and export.
2. Open the export folder, then start the executable.

**Expected:**
- After 1: `outputs.machine.json` sits in the export's `.meta` folder beside the setup files, and
  the console reports that this machine's display bindings were exported.
- After 2: no startup dialog appears. The bound output opens full-screen on its display. With two
  outputs bound to two displays, both open at once and each shows its own canvas.
- The log names each output and the display it presents on.

**Also:** set Player Mode to Installation on a project whose outputs are all unbound and export.
The console warns that there are no bindings to ship, and the export still succeeds.

## Step: Demo mode leaves the bindings behind

**Action:**
Set **Player Mode** back to **Demo**, export, and start the executable.

**Expected:**
- No `outputs.machine.json` in the export's `.meta` folder, however the outputs are bound here.
- The startup dialog appears, and the player shows the first output in the window at the chosen
  resolution — on whichever display is picked, not the one the output is bound to at home.

## Step: A project without a setup is unaffected

**Action:**
Export a project that has no output setup and start it.

**Expected:**
- No `.meta` folder is created beside the executable, the player starts normally, and its log
  notes that no output setup was shipped.
