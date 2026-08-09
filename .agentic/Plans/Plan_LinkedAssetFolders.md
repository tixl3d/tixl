# Plan: Linked Asset Folders and Folder-Import Dialog

Goal: make TiXL usable for video editing without duplicating large footage folders
into a project's `Assets/`. Dropping a **single folder** onto the editor opens a
dialog with three choices; the third mounts the folder without copying.

## Design

### `.tixlLink` marker files

A linked ("mounted") external folder is represented by a small JSON file inside
the project's `Assets/` tree — not by a filesystem junction/symlink (bad with
OneDrive, scary delete semantics, not cross-platform) and not by central project
metadata (would drift from disk state; the asset system is scan-driven).

- File name: `<MountName>.tixlLink` — the basename is the virtual folder name.
- Content: `{ "id": "<guid>", "target": "D:/Footage/shoot", "targetRelative": "../../Footage/shoot" }`
  - `targetRelative` (relative to the package's `Folder`) is tried first, so a
    project moved together with its source folders still resolves; `target` is
    the absolute fallback. `id` is a stable identity for future relinking.
- Extension compared case-insensitively (`.tixllink`), forward slashes in paths.

### Core: mounting (AssetLinkFolder / AssetRegistry)

- Package scan (`AssetRegistry.RegisterAssetsFromPackage`) skips `.tixlLink`
  files as assets and instead mounts them: enumerate the target and register
  every file/dir with a *virtual* address `Package:MountName/rel/path` while
  `FullPath` points at the real external location.
- `Asset` gains `LinkMountId` (Guid, Empty = normal), `IsLinkMountRoot`, and
  `LinkTargetMissing` for the unresolved state (grayed 🔗 in the UI).
- A static mount table maps external roots → virtual address prefixes. Used by
  `TryConvertFilepathToAddress` (reverse lookup) and `TryResolveAddress`'s
  package fallback so unregistered paths under a mount still resolve.
- Each resolved mount gets its own `ResourceFileWatcher`; created/deleted/renamed
  events trigger a full remount of that mount (cheap, avoids partial-state bugs).
- Creating/deleting a `.tixlLink` file at runtime mounts/unmounts via the
  project's existing assets watcher.

### Editor: FolderImportDialog

Intercept in `ImGuiDx11RenderForm.OnDragDrop`: a drop of exactly one directory
opens the dialog (targeting the focused editable project) instead of arming the
ExternalFile drag. Options:

1. **Copy folder** into `Assets/<FolderName>/` (shows file count + total size,
   computed on a background task; copy also runs in the background with a
   progress bar).
2. **Copy sorted by type** — each file goes to `Assets/<AssetType.Subfolders[0]>/`.
3. **Link folder** — writes the `.tixlLink` file and mounts. The dialog explains:
   deleting files inside the linked folder deletes the originals; new files
   (e.g. proxies) appear in the source folder. Warns when the target sits inside
   a Windows-managed folder (OneDrive risk, via `SyncToolConflicts`).

### Asset library UI

- Mount roots draw `Icon.Link` (blue `StatusAutomated` = "linked"; magenta
  `StatusAttention` + tooltip when the target is missing).
- Context menu on a mount root: "Remove link" deletes only the `.tixlLink` file.
  Rename renames the link file, never the external folder.
- "Relink..." opens a modal that takes a pasted folder path (Explorer's "Copy as
  path"), validates it live, then rewrites and remounts the link file. The link
  file keeps a `Targets` list of absolute candidates, most recently linked first,
  so a project moved between machines only needs one relink per machine.

## Deferred / follow-ups
- **Collect assets on export**: exported executables do not bundle linked
  external files yet — needs a consolidate step in the export pipeline.
- Proxy generation writing into mounts must resolve output paths through the
  registry (mount-aware) rather than composing under `AssetsFolder`.
- Nested links inside a mounted target are ignored (no recursion/cycles).

## Test set

See [.tests-manual/asset-folder-import-dialog.md](../../.tests-manual/asset-folder-import-dialog.md).
