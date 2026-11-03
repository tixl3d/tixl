The home view that lists your projects and lets you open, create, or manage them.

## Projects

A project is a folder with a `.csproj` file that holds your operators (`Symbols/`), their source code, and the assets they use. TiXL finds projects by scanning the **project folders** listed in [ui:Settings] → Projects; by default that is a versioned `TiXL` folder in your Documents. Folders that can't be found are flagged with a warning above the list.

Use **Add Project...** to create a new one. The gear icon next to it opens the project settings.

## Loading

Click a project to load and open it. Loading compiles the project and can take a moment; projects marked **(loaded)** switch instantly. To save GPU memory, only the few most recently shown projects stay loaded (see **Keep Projects Loaded** in the settings). Right-click → **Unload Project** frees one manually.

## Archiving

Right-click → **Archive Project** hides a project from the list without deleting anything: it is not loaded or compiled on startup anymore. Archived projects are listed below the active ones; right-click → **Reactivate Project** brings one back. Loading a reactivated project might require a restart.

## Backups

TiXL writes a backup of your projects every few minutes into `.temp/Backup/` inside the project folder. Right-click a project → **Restore from Backup** to roll back to an earlier version; the current state is backed up first, so a restore can be undone. Projects that fail to load are listed under **Broken**, where you can restore a backup the same way.

Right-click → **Share Project...** exports a copy of a project to pass on to others.
