---
id: show-folder-and-recent-sessions
title: Show Folder menu and recent sessions
scope: help-menu
tags: [diagnostics]
added: 2026-11-03
added-in-version: 4.4
prerequisites:
  - At least one earlier session in the log folder.
related-help:
  - ../.help/docs/install/FilesAndFolders.md
---

Verifies that **Help > Show Folder** opens TiXL's folders, and that its **Logs** submenu lists recent sessions, flags the ones that crashed or ended unexpectedly, and can share them.

## Step: Open the folders

**Action:**
Open **Help > Show Folder**. Hover each entry, then click **Settings**.

**Expected:**
- Entries: Projects, Current Project (only while a project is open), Settings, Cache, Temp, and a Logs submenu.
- Hovering shows the full path as a tooltip.
- Clicking opens the folder in the system file manager. A folder that does not exist yet logs a warning instead.

## Step: List recent sessions

**Action:**
Open **Help > Logs**.

**Expected:**
- **Open Log Folder** at the top, then a **Recent Sessions** list with relative times like `2 min ago`, newest first.
- The running session is not listed.
- Clicking a normal session opens its log. Hovering shows when it started and how long it ran.

## Step: Session that ended unexpectedly

**Action:**
Start TiXL, open any project, and kill the process (`kill -9 <pid>` or the task manager). Restart TiXL and open **Help > Logs**.

**Expected:**
- The killed session has a magenta warning icon and reads `... ago   Ended Unexpectedly`.
- Its submenu offers **Open Log**, **Show in File Browser** and **Share with Developers...**.
- No dialog about it appeared at startup.

## Step: Another editor still running

**Action:**
Start a second TiXL instance of the same version while the first one is still running. In the second one, open **Help > Logs**.

**Expected:**
- The first instance's session is not listed as ended unexpectedly. It is not listed at all.

## Step: Share a session

**Action:**
On the flagged session pick **Share with Developers...**, type a note, and click **Share**.

**Expected:**
- The dialog names what is sent (the crash report, the log, or both) and mentions that logs can contain folder and project names.
- After sharing, a short "Shared with the developers" message appears in the menu bar.
- Reopening the submenu shows a checked, disabled **Shared with Developers**. It stays that way after a restart.
- The event appears in Sentry with the log attached and the note under `UserComment`.

## Step: Crash with and without a debugger

**Action:**
1. Start a Debug build from the IDE with the debugger attached and trigger an unhandled exception.
2. Start the same Debug build from a file manager or terminal (no debugger) and trigger the exception again.
3. Restart and open **Help > Logs**.

**Expected:**
- With the debugger: the exception breaks in the IDE, no crash dialog appears, and no startup popup follows.
- Without the debugger: the crash dialog appears as in a release build, and the next start offers to send the report.
- Both sessions are listed with their exception type and a crash report, and **Share with Developers...** is enabled.
- Shared events arrive in Sentry with the crash's own stack trace, tagged `Configuration: Debug`.
