# Install on Linux

This page covers the native Linux version of TiXL: what your system needs, how to download and run it,
and what doesn't work on Linux yet. If the native version doesn't run on your machine, you can still
[run the Windows version under Wine](#run-the-windows-version-under-wine).

The native Linux version is a preview. Expect rough edges, and please report problems on
[GitHub issues](https://github.com/tixl3d/tixl/issues).

## System requirements

- A 64-bit x86 PC (`x86_64`). ARM machines like Raspberry Pi or Apple Silicon aren't supported.
- A graphics card with a Vulkan driver: Mesa (AMD, Intel) or NVIDIA's proprietary driver.
- A distribution from late 2023 or later: Ubuntu 24.04, Debian 13, Fedora 39, Linux Mint 22, or a current Arch.
- The **.NET 10 SDK**. TiXL compiles operators while it runs, so the runtime alone isn't enough.
- The **Slang shader compiler** (`slangc`). TiXL uses it to compile shaders for Vulkan.

## Install the dependencies

Install the .NET 10 SDK and Vulkan support with your package manager. Most desktop installs already
have the Vulkan driver, but the commands below make sure.

### Arch, Manjaro, EndeavourOS

```bash
sudo pacman -S dotnet-sdk vulkan-icd-loader
```

For Slang, install `shader-slang-bin` from the AUR, for example with `yay -S shader-slang-bin`.

### Ubuntu, Debian, Linux Mint, Pop!_OS

```bash
sudo apt install dotnet-sdk-10.0 libvulkan1 mesa-vulkan-drivers
```

If `apt` can't find `dotnet-sdk-10.0`, your release doesn't ship it yet. On Ubuntu, add the .NET
backports archive with `sudo add-apt-repository ppa:dotnet/backports` and try again. On Debian, follow
[Microsoft's instructions for Debian](https://learn.microsoft.com/dotnet/core/install/linux-debian).

### Fedora

```bash
sudo dnf install dotnet-sdk-10.0 vulkan-loader mesa-vulkan-drivers
```

### NixOS

Add `dotnetCorePackages.sdk_10_0`, `vulkan-loader` and `shader-slang` to your packages, and enable
`hardware.graphics.enable = true;` in your configuration.

### Slang on other distributions

If your distribution doesn't package Slang, download the release TiXL is tested with and unpack it
into the folder TiXL looks in first. No `PATH` changes needed:

```bash
mkdir -p ~/.local/opt/slang-2026.18
curl -L https://github.com/shader-slang/slang/releases/download/v2026.18/slang-2026.18-linux-x86_64.tar.gz \
  | tar xz -C ~/.local/opt/slang-2026.18
```

If you keep `slangc` somewhere else, put it on your `PATH` or point the `TIXL_SLANGC` environment
variable at it.

## Download and run TiXL

1. Download the latest `tixl-<version>-linux-x64.tar.gz` from the
   [releases page](https://github.com/tixl3d/tixl/releases), under **Assets**.
2. Unpack it wherever you like to keep applications, for example `~/Apps`:

   ```bash
   mkdir -p ~/Apps
   tar xzf ~/Downloads/tixl-*-linux-x64.tar.gz -C ~/Apps
   ```

3. Start TiXL:

   ```bash
   ~/Apps/tixl-*-linux-x64/TiXL
   ```

TiXL brings its own copy of the .NET runtime, so it always starts, even before the SDK is installed.

### Missing dependencies

At startup, TiXL checks for the .NET SDK, the Vulkan loader and `slangc`. If one is missing, a dialog
lists it together with the install command for your distribution:

- Without Vulkan, TiXL can't render and quits after the dialog.
- Without the .NET SDK or `slangc`, you can choose **Continue**. The built-in operators still load, but
  your own operators won't compile without the SDK, and shaders won't compile without `slangc`.

The same messages also appear in the log, so you can check them later.

### Add TiXL to your app menu

The download contains a desktop entry and an icon in its `share` folder. To add TiXL to your
desktop's app menu, link the program and copy both files into your home folder:

```bash
cd ~/Apps/tixl-*-linux-x64
mkdir -p ~/.local/bin
ln -sf "$PWD/TiXL" ~/.local/bin/tixl
install -Dm644 share/applications/app.tixl.TiXL.desktop ~/.local/share/applications/app.tixl.TiXL.desktop
install -Dm644 share/icons/hicolor/256x256/apps/app.tixl.TiXL.png ~/.local/share/icons/hicolor/256x256/apps/app.tixl.TiXL.png
```

The menu entry starts `tixl`, so `~/.local/bin` must be on your `PATH`. It is by default on most
distributions; log out and back in if the entry doesn't start right away.

## Where TiXL keeps your files

TiXL never writes into the folder you unpacked it to:

- **Projects** go to `~/Documents/TiXL<version>/` — or wherever your desktop puts Documents.
- **Settings and logs** go to `~/.config/TiXL<version>/`. Log files are in its `Log` folder.
- **Caches** go to `~/.cache/TiXL<version>/`, and scratch files to `/tmp/TiXL<version>/`. Both are safe
  to delete.

You can add more project folders under *Settings → Projects → Project Directories*. See
[Files and folders](FilesAndFolders.md) for the full list and what is safe to remove.

## Update and uninstall

To update, unpack the new version next to the old one and run it. If you set up the app menu, repeat
the `ln -sf` command from the new folder so the menu starts the new version. A new minor version
(for example 4.3 to 4.4) starts with fresh settings, because the settings and projects folder names
contain the version. If your projects don't show up after such an update, add the previous version's
projects folder under *Settings → Projects → Project Directories*.

To uninstall, delete the unpacked folder, `~/.local/bin/tixl`, and the two files you copied into
`~/.local/share`. Your projects and settings stay in `~/Documents` and `~/.config` until you delete
them yourself.

## What doesn't work on Linux yet

A few features rely on Windows-only libraries:

- Computer vision operators based on OpenCV and MediaPipe.
- NDI input and output, webcams and screen capture.
- Recording what other programs play (loopback). Live input from a microphone or line-in works.
- Spout video sharing, which only exists on Windows.
- SpaceMouse support, gamepads and Ableton Link.

[Platform support](PlatformSupport.md) has the full comparison of Windows, Linux and macOS.

## Troubleshooting

### Nothing happens when I start TiXL from the app menu

Start it from a terminal instead. TiXL prints its startup log there, which usually shows what went
wrong. Running `~/Apps/tixl-*-linux-x64/TiXL` directly also rules out problems with the menu entry.

### TiXL closes immediately without printing anything

A crash inside a graphics driver kills the process before it can write a log or show a message, so there
is nothing in `~/.config/TiXL<version>/Log/` and nothing on the terminal. On a systemd distribution the
crash is still recorded:

```bash
coredumpctl list TiXL     # was there a crash, and when
coredumpctl info TiXL     # the stack trace of the most recent one
```

If `coredumpctl` reports nothing, TiXL exited on purpose rather than crashing — check the log folder and
the terminal output again, and see [Missing dependencies](#missing-dependencies).

### "version `GLIBC_2.38' not found"

Your distribution is older than TiXL supports. Upgrade to one of the releases listed under
[System requirements](#system-requirements), or use the
[Wine setup](#run-the-windows-version-under-wine).

### TiXL can't find a graphics device

Check that Vulkan works outside TiXL. Install `vulkan-tools` and run `vulkaninfo --summary`. It
should list your graphics card. If it doesn't, install the Vulkan driver for your card (see
[Install the dependencies](#install-the-dependencies)).

### Shader operators show compile errors

TiXL is tested with Slang 2026.18. Other versions usually work, but if shaders fail to compile, try
the tested version as described in [Slang on other distributions](#slang-on-other-distributions).

## Run the Windows version under Wine

If the native version doesn't work for you, you can run the Windows version of TiXL under Wine.

Before installing TiXL, install Microsoft's .NET certificate package. This makes sure your own
operators compile, because TiXL runs `dotnet` through Wine, which can have certificate issues.

1. Download both `codesignctl.pem` and `timestampctl.pem` from
   [.NET's GitHub repository](https://github.com/dotnet/sdk/tree/main/src/Layout/redist/trustedroots).
2. Install the certificates system-wide, as described in the
   [Arch wiki's guide to adding a trusted certificate](https://wiki.archlinux.org/title/User:Grawity/Adding_a_trusted_CA_certificate).

### With Bottles (recommended)

1. Install [Bottles](https://usebottles.com/), either via Flatpak or your distribution's package manager.
2. Create a new Wine prefix using the **Gaming** preset. At the time of writing, the default runner is `soda-9.0-1`.
3. Go to **Options → Dependencies** and install `powershell_core`.
4. Download the TiXL installer from the [releases page](https://github.com/tixl3d/tixl/releases); that's the `.exe` under **Assets**.
5. Place the installer somewhere inside your Wine prefix:
    - In Bottles, click the three dots next to the power icon in the toolbar, then **Browse Files...**. That folder is your prefix's `C:` drive; bookmarking it in your file manager helps.
    - `$WINE_PREFIX/drive_c/users/Public/Desktop` works well, for example.
6. In Bottles, click **Run Executable...** and run the TiXL installer. It also installs the .NET 10 SDK.
7. When it's done, let it start TiXL, or run the executable via Bottles. Unless you changed the default location, it's in `$WINE_PREFIX/drive_c/TiXL`.
8. Add the program executable as a shortcut, then click the three dots next to its entry and select **Add Desktop Entry**. TiXL now shows up with your other apps.

### With system Wine

Tested with Wine 11.2. The commands below use a dedicated Wine prefix, so TiXL doesn't affect your
default `~/.wine` prefix.

Install (adjust the version to the installer you downloaded):

```bash
export WINEPREFIX=~/.wine-tixl
# powershell_core might not be needed, but it avoided issues in some setups
winetricks d3dcompiler_47 powershell_core
wine ~/Downloads/Tixl-v4.0.6.1.exe
```

Run (adjust the version accordingly):

```bash
WINEPREFIX=~/.wine-tixl wine "$WINEPREFIX/drive_c/Program Files/TiXL/TiXL 4.0.6.1/TiXL.exe"
```

### Troubleshooting under Wine

If TiXL starts but crashes or fails to compile a shader when you select a shader operator, install
`d3dcompiler_46.dll` from the Bottles **Dependencies** list:

![Bottles dependency list with d3dcompiler_46 selected](https://github.com/user-attachments/assets/8a0186f3-f506-403f-add8-edccadba7f55)

Rendering to video doesn't work under Wine yet; see
[the pull request that tracks it](https://github.com/tixl3d/tixl/pull/912).

## See also

- [Installation](Installation.md) for Windows
- [Set up a development environment](InstallDev.md) to build TiXL from source
