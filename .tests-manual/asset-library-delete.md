---
id: asset-library-delete
title: Deleting Assets from the Asset Library
scope: asset-library
tags: [assets]
added: 2026-08-07
added-in-version: 4.2
prerequisites:
  - A project with a few disposable asset files (e.g. copies of images) in its Resources folder.
---

Verifies deleting files and folders from the Asset Library with confirmation, multi-selection stats,
and recycle-bin behavior on Windows.

## Step: Deleting a single file

**Action:**
Right-click an unused asset file and choose "Delete file...", then confirm.

**Expected:**
- A modal shows "Delete 1 file (…)" with its size, and notes that files go to the Windows Recycle Bin.
- After confirming, the file disappears from the library and can be found in the Recycle Bin.

## Step: Deleting a multi-selection

**Action:**
Ctrl-click several files to select them, right-click one of them and choose "Delete N selected...".

**Expected:**
- The menu label shows the selected count.
- The confirmation shows the total file count and combined size.
- Cancel leaves everything untouched; confirming removes all selected files.

## Step: Deleting a folder

**Action:**
Right-click a folder with content and choose "Delete folder...", then confirm.

**Expected:**
- The confirmation counts the files inside the folder recursively and shows their total size.
- Confirming moves the folder to the Recycle Bin.
