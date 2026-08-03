# Installation

This page covers the latest version of TiXL (v4.x). For Tooll v3.9.x, see the legacy wiki (Tooll3 installation page, not yet migrated).

Download the [latest release](https://github.com/tixl3d/tixl/releases) and run the installer.

## System requirements

- Windows 10 or 11
- DirectX 11.3-compatible graphics card (GTX 970 or later recommended)

The installer bundles all required dependencies, including .NET and Windows Graphics Tools. Start the installer, dismiss the untrusted-source warning, and proceed.

## Sync tools and the projects folder

By default, TiXL stores projects in your Documents folder. On many Windows machines, sync tools like OneDrive manage that folder without users being aware of it. Syncing can lock project files while TiXL reads or saves them, which makes projects fail to load — they then appear as "Broken" in the project list.

When TiXL detects this, it shows a **Could not load Project** dialog after startup. The suggested fix moves the affected project folders to a location outside the synced folders (for example `C:\TiXL\`) and restarts the editor. You can also move a projects folder manually and add its new location under *Settings → Projects → Project Directories*.

## Non-Windows systems

A native port to Linux and macOS is in progress. Until then you can run TiXL under a translation layer:

- [Install on Linux](InstallLinux.md)
- [Install on macOS](InstallMacOS.md)

## Run TiXL from an IDE

For developing and debugging C# operators, set up a development environment and run TiXL from an IDE. See [Set up a development environment](InstallDev.md).

## Build with command line .Net SDK 

If you don't want to run from an IDE you can clone the repository or download the [zip](https://github.com/tixl3d/tixl/archive/refs/heads/main.zip)
Make sure you have the .net Sdk installed. 

### Release mode
Within the root folder of the solution open a terminal and run `dotnet build --configuration Release`  
Go to \Editor\bin\Release\net10.0-windows folder and run Tixl.exe

### Debug mode
Within the root folder of the solution open a terminal and run `dotnet build`  
Go to \Editor\bin\Debug\net10.0-windows folder and run Tixl.exe
