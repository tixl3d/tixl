# Backups

By default TiXL saves a backup of every user project every 3 minutes. Each backup is a zip archive of the project's source files (operators, settings, shaders, code, assets, thumbnails). Build output (`bin/`, `obj/`), exports (`Export/`), rendered videos (`Render/`), image sequences and screenshots are not included.

If nothing in a project has changed since the last backup, TiXL does not write a duplicate copy — it just refreshes the timestamp on the most recent zip.


## Backup locations

In **TiXL** every user project has its own backup folder:

```
<your-project-folder>/.temp/Backup/
```

The `.temp` folder is excluded from `git` and is safe to delete at any time.

(Legacy: in **Tooll3** all backups were kept in `.t3/backup`.)


## How to restore a backup

Each zip archive is a complete copy of the project's source files. To restore one manually:

1. Close TiXL.
2. Open `<your-project-folder>/.temp/Backup/` and pick the zip you want to restore (filenames carry an index and a timestamp, e.g. `#00042-2026_04_26-15_30_22_123.zip`).
3. Delete the project's `bin/` and `obj/` folders so stale build output does not clash with the restored sources.
4. Extract the zip directly into `<your-project-folder>/`, overwriting existing files.
5. Restart TiXL.

After a crash, TiXL also offers to restore the latest backup of every user project automatically on the next startup. The build artifact cleanup is done for you in that case.
