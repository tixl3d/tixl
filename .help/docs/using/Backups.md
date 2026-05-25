# Backups

By default TiXL saves a backup of every user project every 3 minutes. Each backup is a zip archive of the project files.

## Backup contents

TiXL has two backup modes, controlled by the **Enable Minimal Backup** checkbox in Settings:

- **Full backup** — every project file except build output (`bin/`, `obj/`), exports (`Export/`), rendered videos (`Render/`), image sequences and screenshots. Includes binary assets (textures, meshes, audio).
- **Minimal backup** *(default)* — only source-code-shaped files: `.csproj`, `.cs`, `.t3`, `.t3ui`, `.hlsl`, `.json`, `.txt`. Skips binary assets. Much smaller zips, but if you lose the original project folder the assets are gone too — keep your `Assets/` under separate version control or backup.

The **first backup of a project is always full**, regardless of the toggle, so you have at least one complete snapshot on disk.

Minimal-mode backups carry a `-minimal` suffix in their filename so you can tell them apart at a glance:

```
#00042-2026_04_27-15_30_22_123.zip          ← full backup
#00043-2026_04_27-15_33_45_201-minimal.zip  ← minimal backup
```

## Deduplication

If a fresh backup is byte-identical to the previous one, TiXL drops it and just refreshes the timestamp on the existing archive. Files that external tools rewrite without changing their content (e.g. `Assets/shadertoolsconfig.json`) are excluded so they don't defeat the dedup.

## Backup locations

### Since v4.2

In **TiXL** every user project has its own backup folder:

```
<your-project-folder>/.temp/Backup/
```

The `.temp` folder is excluded from `git` and is safe to delete at any time.

### v4.0

Backups were stored in
```
<HomeDir>/AppData/Roaming/Tixl/Backup/
```

### Before v3.9 (**Tooll3**)

In all backups were kept in `.t3/backup`.

## How to restore a backup

Each zip archive is a complete copy of the project's source files (and assets, if it was a full backup). To restore one manually:

1. Close TiXL.
2. Open `<your-project-folder>/.temp/Backup/` and pick the zip you want to restore. Filenames carry an index and a timestamp, e.g. `#00042-2026_04_26-15_30_22_123.zip`. The `-minimal` suffix marks reduced backups.
3. Delete the project's `bin/` and `obj/` folders so stale build output does not clash with the restored sources.
4. Extract the zip directly into `<your-project-folder>/`, overwriting existing files.
