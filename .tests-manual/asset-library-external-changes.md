---
id: asset-library-external-changes
title: Asset Library Sync with External File Changes
scope: asset-library
tags: [essential, edge]
added: 2026-06-10
added-in-version: 4.3
prerequisites:
  - An editable project is open and its Assets window is visible.
  - A file manager (e.g. Windows Explorer) is open at the project's `Assets/` folder.
---

Verifies that the Assets window picks up files that are added, deleted, or renamed
outside of TiXL — e.g. by copying files in Windows Explorer or exporting directly
from another application like Blender. No operator needs to reference any file for
this to work.

## Step: Adding a file externally

**Action:**
In Windows Explorer, copy any image file (e.g. a `.png`) into the project's `Assets/` folder while TiXL is running.

**Expected:**
- Within about a second, the new file appears in the Assets window without any interaction in TiXL.

## Step: Adding a file to a subfolder

**Action:**
In Windows Explorer, create a new subfolder inside `Assets/` and move the image into it.

**Expected:**
- The new folder appears in the Assets window tree.
- The image is listed inside it; its previous top-level entry is gone.

## Step: Deleting a file externally

**Action:**
In Windows Explorer, delete the image file from the subfolder.

**Expected:**
- The file disappears from the Assets window within about a second.

## Step: Renaming a file externally

**Action:**
Copy the image into `Assets/` again and rename it in Windows Explorer.

**Expected:**
- The Assets window shows the file under its new name; no stale entry with the old name remains.
