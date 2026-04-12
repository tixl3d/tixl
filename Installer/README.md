# Building a TiXL Installer

We use [Inno Setup](https://jrsoftware.org/isinfo.php) to generate a `.exe` installer that includes all dependencies and installs the Windows Graphics Tools.

## Quick Build (One Click)

1. Install [Inno Setup](https://jrsoftware.org/isdl.php).
2. Open `Installer/installer.iss` in Inno Setup.
3. Click the Play button.

This automatically runs `build-release.ps1` which publishes the Player and builds the full solution in Release mode before packaging the installer. The output is created at `Installer/Output/`.

## Manual Build (Step by Step)

If you need to build without Inno Setup, or want to run steps individually:

```powershell
# From the repository root:
pwsh Installer/build-release.ps1
```

This runs:
1. `dotnet restore`
2. `dotnet publish` for the Player (self-contained, win-x64) to `Player/bin/ReleasePublished/`
3. `dotnet build -c Release` for the full solution (Editor post-build copies the published Player into its output)

The result is a complete build in `Editor/bin/Release/net10.0-windows/`.

## Dependencies

The build script automatically downloads these into `Installer/dependencies/downloads/` if not already present:

* [.NET SDK 10.0.201](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
* [Visual C++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)

The installer bundles them and installs them on the user's machine if needed.
