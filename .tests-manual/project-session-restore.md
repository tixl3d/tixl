---
id: project-session-restore
title: Project Session Restore
scope: projects
tags: [user, startup, layouts]
added: 2026-10-28
added-in-version: 4.4
prerequisites:
  - A project with at least one nested operator (an operator you can open with a double click) and a few operators inside it.
  - "Reopen Last Project on Startup" and "Save Layout with Projects" are enabled in the settings.
related-help:
  - ../.help/docs/install/FilesAndFolders.md
---

Verifies that closing and restarting the editor resumes the last project where it was left:
open operator, selection, zoom, window layout and navigation history.

## Step: Restarting resumes the last project

**Action:**
Open the project from the hub. Dive into a nested operator, select two operators in it, and zoom
to an unusual spot. Hide the Parameters window and resize the Output window. Quit TiXL with
`Ctrl+Q` and choose **Exit** without saving, then start TiXL again.

**Expected:**
- While the project loads, a blank screen shows "Loading last session..." with the hint
  "Can be disabled in Settings." below it; the project hub doesn't flash up.
- TiXL starts with the project open instead of the project hub.
- The graph shows the nested operator with the same two operators selected, at the same zoom.
- The Parameters window is still hidden and the Output window keeps its size.
- The project has no unsaved changes (no dot in the top bar).

## Step: Navigation history survives the restart

**Action:**
Select a few operators one after the other, in different compositions. Restart TiXL, then press
`Alt+Left` several times and `Alt+Right` to go forward again.

**Expected:**
- `Alt+Left` steps back through the operators selected before the restart.
- `Alt+Right` returns along the same path.

## Step: Closing to the hub starts in the hub

**Action:**
Close the project to return to the project hub, quit TiXL, and start it again.

**Expected:**
- TiXL starts in the project hub.
- Opening the project from the hub resumes it where it was closed, as in the first step.

## Step: The setting turns reopening off

**Action:**
Open the project, turn off **Reopen Last Project on Startup** in the settings, and restart TiXL.

**Expected:**
- TiXL starts in the project hub.
- Opening the project still resumes where it was left.

## Step: A failed reopen falls back to the hub

**Action:**
With reopening enabled and the project open, quit TiXL. In the settings folder, create an empty
file named `reopeningLastProject` (this is what a crash during the previous reopen leaves behind),
then start TiXL.

**Expected:**
- TiXL starts in the project hub.
- The log contains a warning that reopening did not complete last time.
- Opening the project and restarting again reopens it normally.
