# Export

Defaults for projects exported as standalone executables.

- **Title / Author** — window title and the author line shown in the startup dialog. Empty falls back to the operator and package name.
- **Window Mode** — whether the executable starts fullscreen or in a window.
- **Preferred Width / Height** — the render resolution the executable starts with. The startup dialog offers the display's native modes instead.
- **Skip Startup Dialog** — start directly with these settings instead of asking for display, resolution and window mode. Viewers can still force the dialog with `--dialog`.
- **Show Log Messages** — open a console window with log output next to the executable.
- **Strip Unused Operators** — ship only operators connected to the exported output (plus auto-playing audio ops) and the libraries they need. Disable if an export misses content.
- **Enable Playback Control** — viewers can skip through time with the cursor keys and pause with space.

The main soundtrack's length defines the exported duration. `Export` at the bottom of the panel exports the current composition (it must sit inside a parent composition and have a Texture2D output); the folder icon opens the last export. File → Export as Executable does the same for the selected operator.
