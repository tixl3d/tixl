# Install on macOS

This page covers the native macOS version of TiXL: what your Mac needs, how to install and start it, and
what doesn't work on macOS yet. If the native version doesn't do what you need, you can still
[run the Windows version under Sikarugir](#run-the-windows-version-under-sikarugir).

The native macOS version is an early preview. Expect rough edges, and please report problems on
[GitHub issues](https://github.com/tixl3d/tixl/issues).

## System requirements

- A Mac with Apple Silicon (M1 or newer). Intel Macs aren't supported.
- macOS 14 Sonoma or newer.
- The **.NET 10 SDK**. TiXL compiles operators while it runs, so the runtime alone isn't enough.
- 16 GB of memory is comfortable. The graphics chip shares the Mac's memory, so large projects use more of it.

Graphics run on Vulkan through MoltenVK, which translates it to Metal. The app brings both along.

## Install the .NET 10 SDK

Download the **macOS Arm64 installer** for the .NET 10 SDK from
[Microsoft's download page](https://dotnet.microsoft.com/download/dotnet/10.0) and run it. TiXL looks for
it in `/usr/local/share/dotnet`, where the installer puts it.

If you use Homebrew, `brew install --cask dotnet-sdk` installs the same package.

## Download and run TiXL

1. Download the latest `TiXL-<version>-osx-arm64.dmg` from the
   [releases page](https://github.com/tixl3d/tixl/releases), under **Assets**.
2. Open the DMG and drag **TiXL** into **Applications**.
3. Start TiXL from Applications or Launchpad.

The app isn't signed with an Apple developer certificate yet, so macOS blocks it the first time. Open
**System Settings → Privacy & Security**, scroll to the message about TiXL and click **Open Anyway**. You
only need to do this once per version.

When TiXL first opens your projects, macOS asks whether it may access your **Documents** folder. Click
**Allow**: TiXL keeps your projects there, and it waits until you answer.

### Missing dependencies

At startup, TiXL checks for the .NET SDK and the Slang shader compiler (`slangc`). If one is missing, a
dialog explains what to do. TiXL offers to download `slangc` for you. Without the .NET SDK the built-in
operators still load, but your own operators won't compile.

### Video playback

TiXL doesn't include FFmpeg on macOS yet, so video operators don't work out of the box. For now you can
use Homebrew's FFmpeg 7:

```bash
brew install ffmpeg@7
```

Homebrew's FFmpeg is licensed differently from the version TiXL ships on other systems, so TiXL only uses
it when you start it with `TIXL_FFMPEG_ALLOW_RESTRICTED=1`:

```bash
TIXL_FFMPEG_ALLOW_RESTRICTED=1 /Applications/TiXL.app/Contents/MacOS/TiXL
```

## Using TiXL on a Mac

- **Cmd** takes the place of **Ctrl** in all shortcuts — `Cmd+C`, `Cmd+Z`, `Cmd+S` — and menus show the
  Mac key names. The physical Ctrl key isn't used yet.
- On a trackpad, pinch to zoom and drag with two fingers to pan the graph.
- **Reveal in Finder** opens Finder with the file selected.

## Where TiXL keeps your files

TiXL never writes into the app:

- **Projects** go to `~/Documents/TiXL<version>/`.
- **Settings and logs** go to `~/Library/Application Support/TiXL<version>/`. Log files are in its `Log`
  folder.
- **Caches** go to `~/Library/Caches/TiXL<version>/`. It's safe to delete.

These folders are hidden in Finder's `Library` folder; press `Cmd+Shift+G` in Finder and type the path to
open one. See [Files and folders](FilesAndFolders.md) for what each folder holds and what is safe to remove.

## Update and uninstall

To update, drag the new version into Applications and replace the old one. A new minor version (for
example 4.3 to 4.4) starts with fresh settings, because the settings and projects folder names contain the
version. If your projects don't show up after such an update, add the previous version's projects folder
under *Settings → Projects → Project Directories*.

To uninstall, move TiXL from Applications to the Trash. Your projects, settings and caches stay in the
folders above until you delete them yourself.

## What doesn't work on macOS yet

- **MIDI** — planned, through Apple's CoreMIDI.
- **Video** without the Homebrew setup above, and hardware-accelerated video export.
- **NDI, webcams and screen capture.** Syphon, the Mac's way of sharing video with other apps, is planned.
- **Geometry shaders.** Apple's graphics chips don't have them. TiXL's own operators don't need them, but a
  custom shader with a geometry stage won't render.
- **Recording what other programs play** (loopback). A virtual audio device like BlackHole can route system
  audio into TiXL's live input.
- MediaPipe, OpenCV, Ableton Link, gamepads and SpaceMouse.

[Platform support](PlatformSupport.md) has the full comparison of Windows, Linux and macOS.

## Run the Windows version under Sikarugir

Sikarugir (formerly "Kegworks") runs Windows programs on macOS through Wine. It gives you the full Windows
feature set, at the cost of a more involved setup and lower performance. [Follow the installation
instructions on the Sikarugir repository](https://github.com/Sikarugir-App/Sikarugir), which in turn requires
either [MacPorts](https://www.macports.org/install.php) or [Homebrew](https://brew.sh/).

### Sikarugir setup

First, setup Sikarugir / Winery:

- install a new engine by pressing the plus icon below the list, selecting the engine and pressing 'Download and install' (`v23.7.1` and `v24.0.7` seem to be working well at the time of writing)
- install the wrapper by pressing 'Update Wrapper'

<img width="516" alt="Screenshot 2025-03-15 at 13 26 53" src="https://github.com/user-attachments/assets/fa1280c2-90f5-475d-80dd-55c8f600ac6b" />

<img width="431" alt="image" src="https://github.com/user-attachments/assets/5625716c-2975-4c62-af1c-911d9d77fd92" />


### Setting up the Sikarugir Wrapper

- click the 'Create new blank wrapper' button
- name the application (for example: Tooll3)
  - This will hang the window for a while. It may take a few minutes while the window is unresponsive.
  - The wrapper app will be placed in your userprofile: `~/Applications/Sikarugir/Tooll3.app`, `/Users/[name]/Applications/Sikarugir/Tooll3.app`
  - Once finished, you will see a dialog confirming the creation and you can open it
- you now are required to install some wintricks, by clicking the Winetricks button:

TiXL:
  - `dlls/d3dcompiler_43`
  - `dlls/d3dcompiler_47`
  - `fonts/corefonts`

Tooll3:
  - `dlls/d3dcompiler_43`
  - `dlls/d3dcompiler_47`
  - `dlls/dotnetdesktop6` ⚠
  - `fonts/corefonts`

<img width="410" alt="image" src="https://github.com/user-attachments/assets/2ab6496d-c644-42c3-ae32-603160e52706" />

<img width="410" alt="image" src="https://github.com/user-attachments/assets/260df6d0-8b70-4a2f-9da0-20fa4ccc2a53" />

<img width="842" alt="image" src="https://github.com/user-attachments/assets/75e1c7ae-706b-4254-9594-b4b8335a9dac" />

### Installing Tooll3

- Download and unzip the current stable Tooll3 release (v3.9.3 at the time of writing): https://github.com/tixl3d/tixl/releases/
- Go to the Application folder (`~/Applications/Sikarugir/`, `/Users/[name]/Applications/Sikarugir/Tooll3.app`)
- Right-click (control click) the app and select the `show package contents` option
- Drag and drop the unpacked Tooll3 folder into `Contents/drive_c/`

<img width="939" alt="image" src="https://github.com/user-attachments/assets/f34c7219-6517-4666-a5e7-f4aed6cc5c5e" />

![out](https://github.com/user-attachments/assets/bce41f3e-3922-4b40-ae5d-22f21bafd3d9)

### Finishing the Wrapper configuration

- Open your created `Tooll3.app` again
- Click on advanced
- Set the executable by pressing the "Browse" button to `"C:\TiXL-v3.9.3\StartT3.exe"` by navigating to the pasted folder
- Set `Direct3D to Metal translation layer` option
- (optionally change the [logo](https://tooll.io/images/T3-logo.png))
- click on 'Test Run'

Tooll3 should now start successfully on your Mac. If you run into any issues, feel free to reach out by creating a [new issue in this repository](https://github.com/tixl3d/tixl/issues/new/choose).

<img width="797" alt="image" src="https://github.com/user-attachments/assets/c3613ca5-8868-4171-8282-1f6d09bc644a" />


### Installing TiXL
- Download latest release exe from https://github.com/tixl3d/tixl/releases
- Find your TiXL container in the `/Applications/Sikarugir/` folder and 
- Double click TiXL

<img width="919" height="439" alt="image" src="https://github.com/user-attachments/assets/81d7a26a-c854-4aad-b41d-c18370c30918" />

- In Configuration run click Install Software.
- Select _Choose Setup Executable_ ...

<img width="619" height="364" alt="image" src="https://github.com/user-attachments/assets/843952ec-90e6-402f-b542-4a36ad3fd6f7" />

- Select your downloaded release (probably in Downloads):

<img width="801" height="449" alt="image" src="https://github.com/user-attachments/assets/51d21c2a-9434-4c5f-8ce9-01b32570b030" />

The TiXL installer will magically appear:

<img width="228" height="162" alt="image" src="https://github.com/user-attachments/assets/ded77742-022a-4dfd-bd97-9f49e2fab85e" />

<img width="599" height="460" alt="image" src="https://github.com/user-attachments/assets/655e7213-bc6c-428f-94fe-ac39c04a52bf" />


Do _not_ change the target folder and click _Next_:

<img width="602" height="465" alt="image" src="https://github.com/user-attachments/assets/681ff474-72aa-48ae-8040-e200aaff2266" />


This will install:

<img width="602" height="464" alt="image" src="https://github.com/user-attachments/assets/3bd386e6-a0f8-4c0e-9f99-0cd3ef53439a" />

If .net9 has already been installed, it will ask to repair. In this case, click _Close_ and confirm with _Yes_:

<img width="658" height="494" alt="image" src="https://github.com/user-attachments/assets/e1f8606b-5b37-4673-812e-a6bb951b0650" />
<img width="665" height="487" alt="image" src="https://github.com/user-attachments/assets/2d4e43e3-f9bc-4d01-a739-992990e04fd7" />


Finally click _Finish_ with the Launch checkbox enabled:

<img width="600" height="463" alt="image" src="https://github.com/user-attachments/assets/ccc93d84-0da7-47d3-b59b-4c605ffc8dc8" />


Change the windows app path by clicking _Browse_:

<img width="691" height="368" alt="image" src="https://github.com/user-attachments/assets/5e7f2a2c-02b0-4bfb-9c74-7af912f96f85" />

Select TiXL:

<img width="1049" height="562" alt="image" src="https://github.com/user-attachments/assets/f181b4f8-804e-47fa-86a8-8c3e7594b1e1" />


Start with _Test Run_.

 
### Optional: Allow Retina rendering

- Open the advanced settings of the wrapper
- Go into the 'Tools' tab
- Click the 'Registry Editor' button
- Open `HKEY_CURRENT_USER\Software\Wine\Mac Driver`
- Right-Click in the main pane `New > String Value`
- Name the key `RetinaMode` and the value `Y` **(capitalization is important)**

<img width="1297" alt="image" src="https://github.com/user-attachments/assets/936c8c32-ae61-422c-a546-3052ebb3cd0e" />

![image](https://github.com/user-attachments/assets/9aea310e-e62d-4f88-ac1c-0ec424b2ef31)

## See also

- [Platform support](PlatformSupport.md)
- [Install on Linux](InstallLinux.md)
- [Files and folders](FilesAndFolders.md)
