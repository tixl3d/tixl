# Exporting content as stand alone executables

# TiXL (v4)

Most of the initial flow didn’t change with some exceptions:

* To export a project with a soundtrack, the soundtrack needs to be located within the project’s `Resources/` folder, e.g.
  `c:\Users\<yourname>\Documents\TiXL\<YourProject>\Resources\<mysoundtrack.mp3>`
  (the precise path depends on your Windows version and language).

With v4.0.6 (2025-09-15)…

* the executable will be created in a folder called `T3Export\`. This location will change in the future.
* the export only ships the operators reachable from the exported output (plus auto-playing audio ops), the assets they reference and the optional libraries they declare. If an export misses content, disable `Strip Unused Operators` in `Project Settings` → `Executable` and export again.

---

# Documentation for Tooll v3.9



Video tutorial [here](https://youtu.be/oW-TuDdLExI?t=208).

## Introduction
Exporting as executable is an automatic process the most of the time works out of the box. This page provides more detail information on how this works. When exporting an operator, TiXL will gather all depending operators types and the link file resources like textures, soundtrack etc. and copy these into a folder called `Export`. It will then add the `Player.exe` executable that will look for the main-operator listed in `ProjectSettings.json`.  

## How to export
To export an executable you first make sure that...

1. You're running TiXL in release mode.
2. That you correctly rebuild the complete solution (including Player)

If you want to export as an executable, then you first have to create a Symbol as well.

Then...
1. For this your operator needs an Texture2d output.
2. Select the operator (you can try this with [Demo_There]
3. Right click → Export as Executable
4. TiXL will create a new directory called "Export" (⚠if it already exists it will remove it first) and copy all required resources, the soundtrack, the libraries and the Player.exe there).

![Animation](https://user-images.githubusercontent.com/1732545/175700494-348644a7-a68f-41d9-b6b8-f3cfd8d612a3.gif)

## Running the executable

The executable `Player.exe` is a stand alone application that handles operator loading, pre-initialization and audio playback.

### Startup dialog

On start, the player opens a small dialog asking for the display, the resolution (the native modes of that display, or a custom size), fullscreen and whether to show log messages in a console window. The defaults come from the project's `Executable` settings (`Preferred Width` / `Height`, `Window Mode`, `Show Log Messages`); the last choice is remembered per executable. Enable `Skip Startup Dialog` in the project settings to start directly with the project defaults — useful for installations. `Title` and `Author` in the same panel set the window title and the dialog header.

The player writes its log files and the remembered startup choice to a `.temp/` folder next to the executable (falling back to the user's app-data folder when that location is read-only).

### Loading screen

After the dialog the player shows a dark loading screen with a progress bar and the latest log line while it loads the operator packages, creates the graph and warms up shaders. `Esc` cancels. When loading completes, the log (and `.temp/loadReport.json`) contains a short report: package / symbol / instance counts, shaders compiled vs. loaded from cache, asset size and the duration of each stage — handy when an export starts slowly.

### Precompiled shaders

The export ships the bytecode of every shader the editor has compiled for the exported operator in `ShaderCache/`, so the first start of the executable does not recompile them. View the operator once in the editor before exporting so its shaders are compiled. The player keeps its own cache in `.temp/ShaderCache/`.

### Command line arguments

```
  --display N    Display to use (1-based, as listed in the startup dialog)
  --width N      Render width in pixels
  --height N     Render height in pixels
  --windowed     Run in a window
  --fullscreen   Run borderless fullscreen on the selected display
  --show-logs    Open a console window with log messages
  --loop         Restart playback at the end of the timeline
  --novsync      Disable vsync
  --no-dialog    Skip the startup dialog and start with the resolved settings
  --dialog       Show the startup dialog even if the project disables it
  --reset        Forget the previously used startup settings
  --help         Display this help screen
```

Switches override the remembered and project settings, so a batch file can enforce a setup:

`player-windowed.bat`:
```
Player.exe --no-dialog --windowed --width 1280 --height 720 --display 2
```

The executable is automatically named after the project's `Title` setting (falling back to `Player.exe`); renaming it manually is also fine.

## Advanced inputs / customization

For advanced use cases you could use the lib.io.file [ReadFile] operator to read your own settings and parse them within your demo-operator on startup. (e.g. to customize texts).


# Some caveats

## Missing resources

TiXL will scan your project operators for string parameters with FilePath property. Note that this will not work if you...
- dynamically create paths by combining strings and connect them to a filepath parameter.
- use the [FilesInDirectories] operator
- Add custom fonts
- Your resources are not located in the `./Resources/` folder or use absolute filepaths like `c:/myfile.mp3`.

In these cases you have to add the files manually to the `Export/Resources/` folder. When exporting, TiXL will warn you about these issues.


## Looking for problems
Here are some things you can try when starting the `player.exe` doesn't yield the expected results.

Look into the `.temp/Log/` directory next to the executable and scan the log files for problems. 


