# Exporting content as stand alone executables

# TiXL (v4)

Most of the initial flow didn’t change with some exceptions:

* To export a project with a soundtrack, the soundtrack needs to be located within the project’s `Resources/` folder, e.g.
  `c:\Users\<yourname>\Documents\TiXL\<YourProject>\Resources\<mysoundtrack.mp3>`
  (the precise path depends on your Windows version and language).

With v4.0.6 (2025-09-15)…

* the executable will be created in a folder called `T3Export\`. This location will change in the future.
* the exported executable might contain more resources than necessary. If you’re concerned about file size, you can remove around 100 MB of unnecessary DLLs and media content. We will address this in an upcoming release.

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

The executable `Player.exe` is a stand alone application that handles operator loading, pre-initialization and audio playback. It comes with a number of command line arguments:

```
c:\Users\pixtur\dev\tooll\t3\Export>player.exe --help
Debug: still::partial - v0.1
Copyright (c) 2021 lucid & pixtur

  --novsync     (Default: false) Disable vsync
  --width       (Default: 1920) Defines the width
  --height      (Default: 1080) Defines the height
  --windowed    (Default: false) Run in windowed mode
  --loop        (Default: false) Loops the demo
  --logging     (Default: true) Show log messages.
  --help        Display this help screen.
```

You can use this to create a batch file to enforce a certain resolution or run in windowed mode:

`player-windowed.bat`:
```
Player.exe --width 1280 --height 720 --windowed true
```

You can rename the executable (e.g. to a Demo title). It will also use the `ProjectSettings.json` to determine the correct soundtrack or audio input source.

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

Look into the `Log/` directory and scan the log files for problems. 


