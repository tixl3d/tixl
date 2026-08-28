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

## Step: Dropping a file out of the linked folder

**Action:**
With the folder still linked (from the previous step), open the *source* folder's subfolder in
Windows Explorer and drag one image file from there onto an empty area of the graph window. Hold
still over the graph for a moment before releasing.

**Expected:**
- While hovering, the label under the cursor reads "Reference files from linked folder..." — not "Import files to..." followed by the project's `Assets/` path.
- On release, an image operator is created and displays the image.
- The Console window logs `Created ... with <ProjectName>:<FolderName>/<Subfolder>/<FileName>` — the linked address, not an absolute `C:/...` path.
- No copy of the file appears in the project's `Assets/` folder on disk, and the Assets window shows the file only once, inside the linked folder.

## Step: Dropping files onto a folder in the Assets window

**Action:**
Pick an image file that is *not* in any linked folder (e.g. on the Desktop). In the Assets window,
expand the project until a nested folder is visible — one at least two levels below the project node,
such as `images/portraits`; if none exists, right-click `images`, choose **Create Sub Folder**, and
rename the resulting "New folder" to `portraits`.
Drag the image from Explorer onto that nested folder row and release.

**Expected:**
- While hovering the row, the tooltip reads "Import files to here...".
- The file appears under `images/portraits` in the Assets window, and on disk at `Assets/images/portraits/<FileName>` — *not* at `Assets/portraits/<FileName>` and not at `Assets/images/<FileName>`.
- Repeating the drop onto the *project* root row puts the next copy directly in `Assets/`, not in `Assets/<ProjectName>/`.

## Step: Dropping files onto the linked folder

**Action:**
Drag the same non-linked image from Explorer onto the linked folder's row in the Assets window.

**Expected:**
- The tooltip reads "Import files to here...".
- The file is copied into the *external* source folder on disk and shows up under the linked folder in the Assets window.
- No `Assets/<FolderName>/` directory is created in the project next to the `.tixlLink` file.

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
- The linked folder shows up grayed out with a magenta link icon; its tooltip explains that the target was not found and says to right-click to relink it.
- Restoring the source folder's original name and restarting shows the linked content again.

## Step: Relinking to the renamed folder

**Action:**
With the source folder still renamed and the linked folder grayed out, right-click the linked folder and choose **Relink...**. In Explorer, right-click the renamed source folder, choose **Copy as path**, then paste it into the dialog's *Folder* field (replacing the pre-filled old path) and press **Relink**.

**Expected:**
- The dialog opens pre-filled with the old, missing path.
- While the field holds a non-existent path, a warning "This folder doesn't exist." is shown and the **Relink** button is disabled.
- After pasting the valid path (the surrounding quotes from *Copy as path* are accepted), the warning disappears and **Relink** becomes clickable.
- On **Relink** the dialog closes, the folder is no longer grayed out, the link icon turns blue, and its files are listed again.
- Pointing the dialog at a folder *inside* the project's own `Assets/` folder shows "This folder is already inside the project's assets folder." and keeps **Relink** disabled.

## Step: Both paths keep working

**Action:**
Close TiXL, rename the source folder back to its original name in Explorer, and restart TiXL.

**Expected:**
- The linked folder resolves immediately and is **not** grayed out — no second relink is needed, because the `.tixlLink` file lists both paths.
- Opening the `.tixlLink` file in a text editor shows a `Targets` list with the most recently relinked path first.
