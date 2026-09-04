# Install on Linux

This guide covers running TiXL on Linux under Wine. Native Linux support is not yet available.

## Prerequisites

Before installing TiXL, install Microsoft's .NET certificate package. This ensures your custom operators compile (TiXL runs `dotnet` via Wine, which can have certificate issues).

1. Download both `codesignctl.pem` and `timestampctl.pem` from [.NET's github repository](https://github.com/dotnet/sdk/tree/main/src/Layout/redist/trustedroots).
2. Install the certificates system-wide [(steps to do that linked here)](https://wiki.archlinux.org/title/User:Grawity/Adding_a_trusted_CA_certificate).

## Option 1: Install with Bottles (recommended)

1. Install [Bottles](https://usebottles.com/) – either via Flatpak or your distro's package manager.
2. Start a new Wine prefix using the 'Gaming' preset - at time of writing, the default runner is `soda-9.0-1`.
3. Go to Options → Dependencies and install `powershell_core` 
4. Download the [TiXL installer](https://github.com/tixl3d/tixl/releases); that's the .exe under "Assets".
5. Place the installer in an accessible location under your Wine prefix:
    - In Bottles, click the three dots next to the power icon in the toolbar, then "Browse Files..." - you'll probably want to pin/bookmark this directory in your file explorer for ease of access. That's your prefix's C: drive.
    - I saved mine in `$WINE_PREFIX/drive_c/users/Public/Desktop`, for example.
6. In Bottles, click "Run Executable..." and run the TiXL installer; this should also install the .NET 10 SDK.
7. Once it's done installing, you can either allow it to launch TiXL automatically or run the executable via Bottles. Unless you changed the default install location, it'll be in `$WINE_PREFIX/drive_c/TiXL`.
8. If all is well, add the program executable as a shortcut, then click the three dots next to its program entry and select "Add Desktop Entry". TiXL should now show up amongst your usual apps 🚀
 
## Option 2. Install with system WINE

(Tested with WINE 11.2)

The following assumes operating in a dedicated WINE prefix. This could be skipped though if you're not afraid of messing your default (`~/.wine`) prefix. If there are issues with the default WINE prefix, you could delete it, and it will be recreated from scratch.

Install
```sh
export WINEPREFIX=~/.wine-tixl
# powershell_core might not be needed, but had some issues some time without it
winetricks d3dcompiler_47 powershell_core
# adjust the version accordingly
wine ~/Downloads/Tixl-v4.0.6.1.exe
```
Run (adjust the version accordingly)
```sh
WINEPREFIX=~/.wine-tixl wine "$WINEPREFIX/drive_c/Program Files/TiXL/TiXL 4.0.6.1/TiXL.exe"
```

# Troubleshooting

### TiXL launches but crashes or fail to compile shader when a shader operator is selected
Install `d3dcompiler_46.dll` from the Dependencies list:

![image](https://github.com/user-attachments/assets/8a0186f3-f506-403f-add8-edccadba7f55)

# Known issues 
* Rendering to video doesn't work [yet](https://github.com/tixl3d/tixl/pull/912).

*** 

# The following instructions are for earlier releases of TiXL (Tooll3).

## 1. Install Wine
PlayOnLinux, Bottles, Q4Wine, Lutris, any Wine application should do.
This guide follows a Bottles installation using Flatpak - on distributions that do not come with
flatpak preinstalled (looking at you, Ubuntu) then you can follow the steps for your distro at
[Flatpak Setup](https://www.flatpak.org/setup/)

## 2. Start a Wine prefix
I used the 'Gaming' preset in Bottles.

![image](https://github.com/user-attachments/assets/67e614e5-6909-4adb-8938-55dca6e87726)
![image](https://github.com/user-attachments/assets/0c9ec77b-d479-41c6-918f-521fb2e69327)

That environment installs Windows 10, DXVK, VKD3D, DX9 DLLs, Microsoft Line Services,
Arial Font and Times New Roman Font. Dotnet 6 will need to be installed as well.

## 3. Install .NET Dependency
Run dotnetcoredesktop6 from the Dependencies list, or download manually from
[Download .NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-6.0.403-windows-x64-installer) and run the executable with the same prefix.

![image](https://github.com/user-attachments/assets/67e197dd-2044-4a0c-98ba-205b5ee873cb)


## 4. Download Tooll3
[Download Tooll3](https://github.com/tixl3d/tixl/releases/tag/v3.9.3)
Download the .zip file and extract it into a VERY specific directory... it needs to go inside the
wine prefix. So, open Bottles and using the 3 dots at the top next to the shutdown icon and
select 'Browse Files' which will open up a file browser at the prefix (C Drive) location. I saved
the extracted tool3.8.1 directory in C/users/Documents just to make it easy to find later.

![image](https://github.com/user-attachments/assets/68521f38-8f0e-4927-a0a1-b9189c2a30ab)


## 5. Run startT3.exe with Bottles
Select ‘Run Executable...’ and navigate to your wine prefix
(`~/.var/app/com.usebottles.bottles/data/bottles/bottles`) and navigate through to find the
startT3.exe file. You may need to show hidden files to find `.var` in your home directory.

![image](https://github.com/user-attachments/assets/62935fbb-2bec-45d4-ab1e-82a8f4e7f527)


## 5(a). Run startT3.exe with the wine file browser.
This will take you straight to the C Drive and from there you can find the startT3.exe executable
and run it with a double click.

## 6. Add a desktop link
After successfully running T3, you can add it to your desktop by selecting the 3 dots next to the
launch button and click ‘Add Desktop Entry’.

![image](https://github.com/user-attachments/assets/bedcbc2a-7db3-48ac-8c46-439768df3ae8)
![image](https://github.com/user-attachments/assets/716dae2f-113e-4d27-9b76-e042b48b9ea3)

