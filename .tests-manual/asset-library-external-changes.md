---
id: asset-library-external-changes
title: Asset Library Sync with External File Changes
scope: asset-library
tags: [user, essential, edge]
added: 2026-06-10
added-in-version: 4.3
prerequisites:
  - An editable project is open and its Assets window is visible.
  - A file manager (e.g. Windows Explorer) is open at the project's `Assets/` folder.
---

When you add, delete, or rename a file in your project's assets outside of TiXL —
say you drop a new texture into the folder from Windows Explorer, or export one
straight from another app like Blender — the [ui:AssetLibrary|Assets window] should notice and update
on its own. This checks that it keeps in step with whatever's in the folder, without
you having to refresh anything.

## Step: Adding a file externally

**Action:**
In Windows Explorer, copy any image file (e.g. a PNG) into the project's `Assets/` folder while TiXL is running.

**Expected:**
- Within about a second, the new file shows up in the Assets window on its own — you don't have to click or refresh anything in TiXL.

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
