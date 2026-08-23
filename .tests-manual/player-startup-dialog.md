---
id: player-startup-dialog
title: Player startup dialog and options
scope: export
tags: [player, export]
added: 2026-08-23
added-in-version: 4.3
prerequisites:
  - A writable project with an operator that has a Texture2D output (e.g. a simple [Blob] wrapped in a symbol) is open.
  - The editor was built in Release so `Player/` exists next to the editor binaries.
  - Ideally two displays are connected; the single-display variant of each step is noted.
related-help:
  - ../.help/docs/using/ExportExecutables.md
---

Verifies the startup dialog of exported executables, the project settings that feed it,
and the command-line switches that bypass it.

## Step: Project settings show the executable options

**Action:**
Open `Project Settings` for the operator you will export and switch to the `Executable` category.

**Expected:**
- The panel shows `Title` and `Author` text fields (placeholders: operator / package name), `Window Mode` (default Fullscreen), `Enable Playback Control`, `Preferred Width` (1920) and `Preferred Height` (1080), `Skip Startup Dialog` (off) and `Show Log Messages` (off).
- Enter `My Demo` as `Title` and `Me` as `Author`; change `Preferred Width` / `Preferred Height` to `1280` / `720`. The reset-to-default affordance appears on the changed fields.

## Step: Export and start with the dialog

**Action:**
Select the operator and run `Export as Executable`. When the success message appears, open the export folder and double-click `Player.exe`.

**Expected:**
- No console window opens.
- A small dialog titled `My Demo` opens centered on the primary display, with `My Demo` and `Me` as header.
- The export folder now contains a `.temp/Log/` folder with the player's log file.
- `Display` lists every connected display as `N: <name> (<width> x <height>)`, the primary one marked `[primary]`.
- `Resolution` is preselected to `1280 x 720` when the display offers that mode, otherwise it shows `Custom...` with two number fields holding `1280` and `720`.
- `Fullscreen` is checked (follows `Window Mode`); `Show log messages` is unchecked.

## Step: Resolution list follows the display

**Action:**
Open the `Resolution` dropdown. If a second display is connected, pick it in `Display` and open `Resolution` again.

**Expected:**
- The list contains the display's native modes, highest first; the display's current mode is suffixed `(native)`.
- Switching the display rebuilds the list; a resolution that exists on both displays stays selected, otherwise the dropdown shows `Custom...`.
- Choosing `Custom...` shows two editable number fields for width and height.

## Step: Start on a chosen display

**Action:**
Pick the secondary display (or keep the primary one on a single-display setup), keep `Fullscreen` checked and press `Start` (or `Enter`).

**Expected:**
- The dialog closes and the content appears borderless on the chosen display, with the mouse cursor hidden.
- `Alt+Enter` toggles to a centered window on the same display and back.
- `Esc` quits the player.

## Step: Last choice is remembered, Quit cancels

**Action:**
Start `Player.exe` again. Then press `Quit` (or `Esc`) in the dialog.

**Expected:**
- The dialog opens with the display, resolution and checkboxes from the previous run.
- `Quit` closes the dialog and nothing else opens; the process ends.

## Step: Command line skips the dialog

**Action:**
From a terminal in the export folder run:

```
Player.exe --no-dialog --windowed --width 800 --height 450 --display 1
```

**Expected:**
- No dialog appears; an 800 x 450 window opens centered on display 1 (the first entry of the dialog's list).
- Run `Player.exe --help`: a message box lists `--display`, `--width`, `--height`, `--windowed`, `--fullscreen`, `--show-logs`, `--loop`, `--novsync`, `--no-dialog`, `--dialog` and `--reset`.

## Step: Show logs opens a console

**Action:**
Run `Player.exe --no-dialog --show-logs`.

**Expected:**
- A console window opens next to the content window and shows the startup log (`Starting <title> ...`, `Startup options: ...`, operator loading messages).

## Step: Project can skip the dialog

**Action:**
Back in the editor, enable `Skip Startup Dialog` in the `Executable` settings, export again and double-click `Player.exe`. Then run `Player.exe --dialog` from a terminal.

**Expected:**
- The first start opens directly with the project's resolution and window mode, no dialog.
- `--dialog` brings the dialog back despite the project setting.
