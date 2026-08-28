---
id: player-export-stripping
title: Export strips unused operators and dependencies
scope: export
tags: [player, export]
added: 2026-08-23
added-in-version: 4.3
prerequisites:
  - A writable project is open. Its editor build is Release so `Player/` exists next to the editor binaries.
  - The `Lib` package is available (it is in every install).
related-help:
  - ../.help/docs/using/ExportExecutables.md
---

Verifies that an exported executable contains only the operators reachable from the exported output,
the optional libraries those operators declare, and no foreign-platform runtimes.

## Step: Build the test graph

**Action:**
Create a new symbol `StripTest` with a `Texture2D` output. Inside, connect a `[Blob]` to the output.
Add a second, **unconnected** `[VideoDeviceInput]` (webcam) operator next to it and an unconnected `[AudioClip]`
with `AutoPlay` on and a short audio file assigned.

**Expected:**
- The graph shows three children; only `[Blob]` feeds the output.

## Step: Export with stripping (default)

**Action:**
Open `Project Settings` → `Executable` and confirm `Strip Unused Operators` is checked. Select `StripTest` and
run `Export as Executable`. Open the editor's Console window and filter for `Export`.

**Expected:**
- The log shows `Collected N instances …`, a line `<project>: stripped 1 unused child operators` and a summary
  `Export copied X files (… MB), skipped Y files (… MB)` with a non-zero skipped size.
- A `Debug` line lists `Skipped optional dependencies: … OpenCvSharpExtern.dll …`.

## Step: Inspect the export folder

**Action:**
Open the export folder `<project>/Export/StripTest/`.

**Expected:**
- `Operators/Lib/runtimes/` contains only `win-x64` (and possibly `win`); no `linux-*`, `osx-*`, `win-x86` folders.
- `Operators/Lib/runtimes/win-x64/native/OpenCvSharpExtern.dll` is absent.
- `Operators/<project>/Symbols/StripTest.t3` lists two children (`Blob` and `AudioClip`); the webcam op is gone.
- `Operators/Lib/Symbols/` only contains the `.t3` files of operators used by `[Blob]` and `[AudioClip]`
  (dozens, not hundreds).

## Step: The export runs

**Action:**
Double-click `Player.exe`, press `Start`.

**Expected:**
- The blob renders and the audio clip plays; no error message box appears.
- `.temp/Log/<timestamp>.log` shows no `Error loading symbol child` warnings.

## Step: Stripping can be disabled

**Action:**
Uncheck `Strip Unused Operators` in `Project Settings` → `Executable` and export again.

**Expected:**
- `Operators/<project>/Symbols/StripTest.t3` lists all three children again and
  `Operators/Lib/runtimes/win-x64/native/OpenCvSharpExtern.dll` is present (the webcam op is shipped, so its
  dependency is kept). Foreign-platform `runtimes/` folders stay excluded either way.
- The export still runs.
