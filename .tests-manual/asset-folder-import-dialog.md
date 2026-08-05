---
id: asset-folder-import-dialog
title: Folder Import Dialog and Linked Asset Folders
scope: asset-library
tags: [user, essential]
added: 2026-08-05
added-in-version: 4.3
prerequisites:
  - An editable project is open and its Assets window is visible.
  - A test folder outside the project (e.g. on the Desktop) containing a few images and videos, ideally with a subfolder.
---

Dropping a single folder onto the editor should open an import dialog with three
options: copy the folder, copy its files sorted by type, or link the folder in
place without copying (a small `.tixlLink` file in `Assets/`). This checks the
dialog and the linked-folder behavior.

## Step: Opening the dialog

**Action:**
Drag the test folder from Windows Explorer anywhere onto the TiXL window.

**Expected:**
- A modal "Import Folder" dialog opens (no files are imported yet).
- It shows the folder name, its path, and after a moment the file count and total size.
- Three options plus Cancel are offered; Cancel closes the dialog without any change.

## Step: Copy folder into Assets

**Action:**
Drop the folder again and choose **Copy folder into Assets**.

**Expected:**
- The Assets window shows a new folder with the dropped folder's name containing all files, subfolders preserved.
- The files on disk are copies inside the project's `Assets/` folder; the source folder is untouched.

## Step: Copy files sorted by type

**Action:**
Drop the folder again and choose **Copy files sorted by type**.

**Expected:**
- Images land in the images subfolder, videos in the video subfolder, etc.
- Files that already exist are skipped (check the console log for a summary).

## Step: Link folder without copying

**Action:**
Drop the folder again and choose **Link folder without copying**.

**Expected:**
- The folder appears in the Assets window with a small blue link icon; hovering it shows the target path.
- A `<FolderName>.tixlLink` file exists in the project's `Assets/` folder; nothing was copied.
- The link file itself is not listed as an asset in the Assets window.
- Files inside the linked folder can be dragged onto the graph like any other asset (e.g. an image creates an image operator that displays correctly).

## Step: External changes to the linked folder

**Action:**
In Windows Explorer, copy a new image into the *source* folder.

**Expected:**
- The new file appears inside the linked folder in the Assets window within a second or two.

## Step: Rename and remove the link

**Action:**
Right-click the linked folder in the Assets window, choose **Rename**, and enter a new name. Then right-click it again and choose **Remove link**.

**Expected:**
- After renaming, the folder appears under the new name and the `.tixlLink` file in `Assets/` is renamed accordingly; the source folder on disk keeps its original name.
- After **Remove link**, the folder disappears from the Assets window and the `.tixlLink` file is gone.
- The source folder and all its files are untouched.

## Step: Missing link target

**Action:**
Link the folder again, close TiXL, rename the *source* folder in Explorer, and restart TiXL.

**Expected:**
- The project loads normally.
- The linked folder shows up grayed out with a magenta link icon; its tooltip explains that the target was not found.
- Restoring the source folder's original name and restarting shows the linked content again.
