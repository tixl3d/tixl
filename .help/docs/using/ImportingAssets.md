# Importing asset folders

Dropping a **single folder** from your file manager onto the TiXL window opens the import dialog. It shows the folder's file count and total size and offers three ways to bring the content into the current project:

- **Copy folder into Assets** — copies everything to `Assets/<FolderName>/`, keeping the folder structure.
- **Copy files sorted by type** — copies the files into the standard subfolders for their type (`images/`, `video/`, `audio/`, ...).
- **Link folder without copying** — the folder shows up in the Assets window but stays where it is on disk.

Dropping individual files copies them into the project, and they can be dropped straight onto the graph or timeline. The one exception is files that already sit inside a linked folder — see below.

## Linked folders

Linking is meant for large media collections — for example a folder of video footage that would be wasteful to duplicate into every project. Instead of copying, TiXL writes a small `<FolderName>.tixlLink` file into the project's `Assets/` folder that points at the real location. The linked folder appears in the Assets window marked with a link icon, and its files can be used like any other asset.

Because the files stay in their original location:

- **Deleting files inside the linked folder deletes the originals.**
- New files created there by TiXL (for example proxies) also appear in the source folder.
- The link only resolves on machines where the target folder exists. On other machines the folder shows up grayed out with a warning icon; the project itself still loads fine.

### Dropping files from a linked folder

Dragging a file that lives below a linked folder — straight from Windows Explorer onto the graph, the timeline, or the Assets window — does **not** copy it. TiXL recognises that the file is already reachable through the link and references it under its linked address, for example `MyProject:Footage/Clips/example.mp4` for a folder linked as `Footage`. The drag indicator says *Reference files from linked folder...* instead of naming a target folder.

If you do want a copy inside the project, copy the file into the `Assets/` folder in Explorer first, or use the folder import dialog's copy options.

To remove a link, right-click the linked folder in the Assets window and choose **Remove link** — this only deletes the `.tixlLink` file and never touches the linked content. Renaming a linked folder in the Assets window renames the link, not the source folder.

### Relinking a moved folder

If the linked folder is missing — the usual reason is opening the project on a second machine where a synced folder sits under a different path — right-click it in the Assets window and choose **Relink...**. Paste the new location into the dialog and confirm.

The quickest way to get the path: in the Windows Explorer right-click the folder, choose **Copy as path** (or press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd>), then paste it into the dialog.

The link file remembers the previous locations as well, so a folder that was relinked once on each machine keeps resolving on all of them — you only pay for the relink the first time.

> [!NOTE]
> If the project lives in a synced folder (OneDrive, Dropbox, ...), the small link file syncs along with it, but the linked media does not. Keep that in mind when opening the project on another machine.

> [!NOTE]
> Exported executables currently do not bundle linked external files. Copy the folder into the project before exporting a stand-alone player.
