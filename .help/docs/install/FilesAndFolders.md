# Files and folders

Where TiXL puts things, what is safe to delete, and what to back up. Useful when you are backing up your work, cleaning up disk space, removing TiXL, or reporting a problem and need to find a log.

## The short version

Four places matter, and they are separate on purpose:

| | What | Back it up? |
|---|---|---|
| **Projects** | your work | **Yes** |
| **Settings** | preferences, themes, key bindings, logs | **Yes** |
| **Cache** | compiled shaders, thumbnails | No — rebuilds itself |
| **Temp** | scratch files for the current session | No — disposable |

Deleting the cache or temp folder costs you nothing but a slower next start. Deleting the other two loses work.

## Where they are

=== "Linux"

    ```
    ~/Documents/TiXL4.4-alpha/      projects
    ~/.config/TiXL4.4-alpha/        settings and logs
    ~/.cache/TiXL4.4-alpha/         cache
    /tmp/TiXL4.4-alpha/             temp
    ```

    These follow the [XDG Base Directory Specification](https://specifications.freedesktop.org/basedir/latest/),
    so `$XDG_CONFIG_HOME` and `$XDG_CACHE_HOME` are honoured if you set them. The projects folder follows your
    desktop's configured Documents directory, so on a localised system it may be `~/Dokumente` instead.

=== "Windows"

    ```
    %USERPROFILE%\Documents\TiXL4.4-alpha\   projects
    %APPDATA%\TiXL4.4-alpha\                 settings and logs
    %LOCALAPPDATA%\TiXL4.4-alpha\            cache
    %TEMP%\TiXL4.4-alpha\                    temp
    ```

    The cache is under `LOCALAPPDATA` rather than `APPDATA` so it does not follow a roaming profile between
    machines.

## Why the version is in the folder name

Every folder carries the version it belongs to, so several TiXL versions can be installed at once without
touching each other's settings or projects. That is deliberate: it lets you keep a stable version for real
work while trying an alpha.

The cost is that upgrading to a new minor version starts with fresh settings. The welcome window's **Import
Settings** and **Import Projects** tabs copy them across.

If you want two builds of the *same* version kept apart, set `TIXL_OVERRIDE_VERSION_ID` — the value replaces
the suffix, so `TIXL_OVERRIDE_VERSION_ID=experiment` gives you `TiXL4.4-experiment`.

## What is in the settings folder

```
userSettings.json        preferences, window layout, recently used projects
projectSettings.json     per-project playback and audio settings
versionMarker.json       the version you last ran, so the welcome window knows what is new
Themes/                  colour themes
KeyBindings/             custom shortcuts
Log/                     one timestamped log per launch, plus crash reports
```

`Log/` is where to look when something goes wrong. Each launch writes `2026_10_06_19_49_00_484.log`, and a
crash additionally writes `crash <date> - <error> - <id>.txt` beside it. When TiXL is started through its
launcher script on Linux, the console output of the most recent two runs is also there as `console.log` and
`console.previous.log` — which is the only record if TiXL dies before its own logging starts.

## What is in a project

```
MyProject/
    MyProject.csproj     the project, compiled when TiXL loads it
    Symbols/             your operators: one .cs, .t3 and .t3ui per operator
    Assets/              images, videos, soundtracks the project uses
    dependencies/        native libraries this project needs
    .meta/               output and projection setups
    .temp/               build output — and Backup/, the auto-backups
    Render/              rendered image sequences and videos
    Export/              exported players
```

Everything in a project folder is yours and travels with it, except `.temp/`. Note that `.temp/Backup/`
holds the automatic backups, so it is the one part of `.temp/` worth keeping — see
[Backups](../using/Backups.md).

## What is in the cache

```
Shaders/            compiled shader bytecode
ShadowCopy/         operator assemblies, copied so the originals can be rebuilt while TiXL runs
Thumbnails/         operator and video thumbnails
SoundtrackImages/   rendered waveform images
```

Safe to delete at any time. The next start is slower while shaders recompile.

## Removing TiXL completely

Uninstall the application, then delete the four folders above. The cache and temp folders can go first and
independently if you only want the disk space back.

If you have used more than one version, there is one set of folders per version — delete the ones whose
version you no longer want.

Two things live outside those folders and are shared with other software, so leave them unless you know you
want them gone: `~/.nuget/packages` (used when compiling operator projects) and, on Linux,
`~/.local/opt/slang-<version>` if you installed the Slang shader compiler there.

## See also

- [Install on Linux](InstallLinux.md)
- [Backups](../using/Backups.md)
