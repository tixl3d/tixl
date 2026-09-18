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

What an executable shows is decided by the [SendToOutput] operators inside the exported operator: each one puts its texture onto the project's [output setup](OutputSetup.md), and the export ships everything that feeds them. No single combined output is needed, and a project's root operator can be exported directly.

1. Make sure the operator contains at least one [SendToOutput]. If it doesn't, the **Export** button in the `Executable` settings is disabled, and **Add SendToOutput** creates one for you to connect.
2. Open the project settings (`Executable`) and click **Export**, or select the operator and choose **Export as Executable** from the menu.
3. TiXL will create a new directory called "Export" (⚠ if it already exists it will remove it first) and copy all required resources, the soundtrack, the libraries and the Player.exe there.

Projects exported before 4.3 used the operator's first texture output instead. To export such a project again, connect that output to a [SendToOutput].

![Animation](https://user-images.githubusercontent.com/1732545/175700494-348644a7-a68f-41d9-b6b8-f3cfd8d612a3.gif)

## Running the executable

The executable `Player.exe` is a stand alone application that handles operator loading, pre-initialization and audio playback.

### Startup dialog

On start, the player opens a small dialog asking for the display, the resolution (the native modes of that display, or a custom size), fullscreen and whether to show log messages in a console window. The defaults come from the project's `Executable` settings (`Preferred Width` / `Height`, `Window Mode`, `Show Log Messages`); the last choice is remembered per executable. Enable `Skip Startup Dialog` in the project settings to start directly with the project defaults — useful for installations. `Title` and `Author` in the same panel set the window title and the dialog header.

The player writes its log files and the remembered startup choice to a `.temp/` folder next to the executable (falling back to the user's app-data folder when that location is read-only).

### Output setup

If the project has an [output setup](OutputSetup.md), its `*.setup.json` files are copied into a `.meta` folder beside the executable and the player loads one at startup, picking the same file the editor would. Operators that read the venue — [StageGeometry], [DrawStageCanvas], [UseProjectorCam] — therefore work in an export exactly as they do in the editor.

What else travels depends on **Player Mode** in the project's `Executable` settings:

- **Demo** — runs anywhere. It asks for a display and a resolution on startup and shows one window, with the setup's first output composited into it. The local bindings stay behind, because the same display numbering names different screens on a different computer.
- **Installation** — runs on the machine it was exported for. That machine's bindings (`outputs.machine.json`) travel with it, so every output bound to a display opens full-screen where it belongs, the first one reusing the player's own window. The startup dialog is skipped; `--dialog` still forces it when someone is there to answer.

An output whose canvas is left at 0 × 0 takes the size of what shows it: its display in an installation, otherwise the resolution chosen in the startup dialog. A project whose sends reach no output yet — no setup, or nothing routed — shows its first [SendToOutput] directly in the window, so a quick export works before the output setup has been touched. A binding naming a display the machine doesn't have is reported in the log and skipped, so an installation says what is wrong instead of coming up dark.

Streams travel the same way. An installation whose outputs are bound to an NDI or Spout plug sends them from the player just as the editor does, and the export includes the package that implements the sender even when no operator in the graph comes from it. A stream whose sender is missing on the target machine is reported once in the log.

To move an installation to another machine, or to rebind at a venue, edit `outputs.machine.json` in the export's `.meta` folder or open the project in TiXL and export again. The player has no binding UI of its own — an installation should come up the same way every time.

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


