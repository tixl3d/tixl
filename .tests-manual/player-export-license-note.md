---
id: player-export-license-note
title: Export shows the BASS license note and ships the license text
scope: export
tags: [player, export, licensing]
added: 2026-10-08
added-in-version: 4.4
prerequisites:
  - A writable project is open. Its editor build is Release so `Player/` exists next to the editor binaries.
  - The project contains an operator with a `[SendToOutput]` inside, so it can be exported.
related-help:
  - ../.help/docs/using/ExportExecutables.md
---

Verifies that users learn about the BASS license before and after exporting, and that the license text
travels with the export.

## Step: Note in the Executable settings

**Action:**
Open the project settings and select the `Executable` category.

**Expected:**
- Above the **Export** button, a muted help text says that exported executables include the BASS audio
  library, that it is free for non-commercial use, and that commercial distribution needs a license from
  un4seen.
- The text wraps within the window and does not push the **Export** button out of view.

## Step: Note in the export result

**Action:**
Click **Export**.

**Expected:**
- The success message box starts with "Exported successfully to" and the export folder, followed by the
  same license note.
- The log contains the same text as an info message.

## Step: License text in the export folder

**Action:**
Open the export folder (the folder icon next to **Export**).

**Expected:**
- A `licenses/` folder exists and contains `BASS-un4seen.txt`.
