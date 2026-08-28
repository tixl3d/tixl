---
id: player-loading-screen
title: Player loading screen and load report
scope: export
tags: [player, export]
added: 2026-08-23
added-in-version: 4.3
prerequisites:
  - An exported executable exists (see `player-startup-dialog`), ideally of a project with a soundtrack so the shader warm-up step runs for a few seconds.
related-help:
  - ../.help/docs/using/ExportExecutables.md
---

Verifies the dark loading screen shown while the player loads, cancelling with Esc, and the load report.

## Step: Loading screen appears right after the dialog

**Action:**
Start `Player.exe`, keep the defaults and press `Start`.

**Expected:**
- The content window opens immediately with a near-black background, the project title in large light text, a
  status line below it (`Loading Lib (2/4)`, `Connecting operators...`, `Creating operators...`, `Warming up shaders and resources (4s / 120s)`, …), a thin progress bar that grows left to right, and the hint `Press Esc to cancel`.
- The bottom of the window shows the most recent log message in small dark-gray text; it changes as packages load and shaders compile (`Compiling <name> @<entry>...`).
- No content frame flashes before the loading screen; the screen stays up until playback starts.

## Step: Esc cancels loading

**Action:**
Start `Player.exe` again and press `Esc` while the progress bar is still moving.

**Expected:**
- The window closes within a moment (the current step finishes first); no crash dialog appears.
- `.temp/Log/<latest>.log` ends with `Loading cancelled.`

## Step: Load report

**Action:**
Start `Player.exe`, let it finish loading, then quit with `Esc`. Open `.temp/Log/<latest>.log` and `.temp/loadReport.json`.

**Expected:**
- The log contains a block starting with `Loaded in <n>s: <p> packages, <s> symbols, <i> instances, <c> shaders compiled, <k> from cache, <a> asset files (<mb> MB)` followed by one indented line per stage (`Load operators`, `Create instances`, `Prepare audio`, `Warm up shaders`) with seconds.
- `loadReport.json` holds the same numbers.
- Already on the first start, `from cache` is close to the total shader count when the export folder contains `ShaderCache/` with entries (the editor had compiled the shaders before exporting); `Warm up shaders` then takes well under a second for graphs without heavy resources.

## Step: Window mode switch during playback still works

**Action:**
After loading completes, press `Alt+Enter` twice.

**Expected:**
- The window toggles to borderless fullscreen and back without a crash (the loading screen's resources were released before playback).
