# Platform support

TiXL started on Windows and is being ported to Linux and macOS. This page lists what works on each system
today and what is still missing, so you can check before you start a project or plan a show on a Mac or a
Linux machine.

Windows is the main platform and has everything. Linux is a native preview, and macOS (Apple Silicon) is an
early native preview. If a feature you need is missing on your system, the Windows version under Wine is an
alternative — see [Install on Linux](InstallLinux.md) and [Install on macOS](InstallMacOS.md).

## How to read the tables

- **Works** — available and tested.
- **Partial** — available with a limitation, explained in the note.
- **Not yet** — not available on this system. Operators for it still show up in the library, but they
  produce no output and write a message to the log.
- **Planned** — not available yet, and work on it is planned.

The status describes the current development version. Please report anything that doesn't match on
[GitHub issues](https://github.com/tixl3d/tixl/issues).

## The basics

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| Editor, graph, timeline, projects | Works | Works | Works |
| 3D rendering, PBR materials, environments | Works | Works | Works |
| Shader compilation | Works | Works | Works |
| Writing and compiling C# operators | Works | Works | Works |
| Exporting a project as an executable | Works | Partial | Partial |
| Image and image-sequence export | Works | Works | Works |
| Video export | Works | Works | Partial |

Windows renders with DirectX 11; Linux and macOS render with Vulkan (on macOS through MoltenVK, which
translates Vulkan to Metal). Shaders are compiled with the Slang compiler on Linux and macOS, which TiXL
offers to download on first start.

On all three systems TiXL needs the **.NET 10 SDK**, not just the runtime: it compiles operators while it
runs.

**Exported executables** run on the system you exported from — there is no export for another system. On
Linux and macOS the exported player keeps the name `Player` instead of the project's title, and operators
that depend on native libraries (for example the SVG operators) may not work in it yet.

**Video export on macOS** works only with the development setup for FFmpeg described under
[Video](#video).

## Rendering details

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| Geometry shaders | Works | Works | Not yet |
| GPU timing in the performance view | Not yet | Not yet | Not yet |
| Warning before the GPU runs out of memory | Works | Not yet | Not yet |
| Overlay while the editor is busy | Works | Not yet | Not yet |

Apple GPUs have no geometry shaders. TiXL's own cube map operators work without them; a custom shader
that uses a geometry stage doesn't render on macOS.

A handful of TiXL's reference renderings still differ slightly on macOS, mostly particle and text-heavy
scenes. These are bugs being worked on, not missing features.

## Video

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| Video playback ([PlayVideo], video clips on the timeline) | Works | Works | Partial |
| Network streams (RTSP) | Works | Works | Partial |
| Hardware-accelerated video export | Works | Not yet | Not yet |
| Webcams and capture cards | Works | Not yet | Planned |
| NDI input and output | Works | Not yet | Not yet |
| Spout (sharing video with other apps) | Works | — | — |
| Syphon (sharing video with other apps) | — | — | Planned |
| Screen capture | Works | Not yet | Not yet |
| Timeline thumbnails of video clips | Works | Not yet | Not yet |

TiXL plays and exports video with FFmpeg. Windows and Linux include it. On macOS there is no included
FFmpeg yet: for now, install it with Homebrew (`brew install ffmpeg@7`) and start TiXL with
`TIXL_FFMPEG_ALLOW_RESTRICTED=1`. Homebrew's FFmpeg is licensed differently from the one TiXL ships, which
is why TiXL doesn't use it without being told to.

Spout only exists on Windows, and Syphon only on macOS; Syphon support is planned.

## Audio

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| Soundtrack playback, mixing, waveforms | Works | Works | Works |
| Audio reactivity from the soundtrack | Works | Works | Works |
| Live input from a microphone or line-in | Works | Works | Works |
| Live input from system audio (loopback) | Works | Not yet | Not yet |

Recording what other programs play (loopback) needs a Windows-only interface. On macOS you can route
system audio through a virtual audio device such as BlackHole and select it as the input.

## Controllers and input devices

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| MIDI input and output, MIDI controllers | Works | Works | Works |
| OSC | Works | Works | Works |
| Art-Net, sACN, DMX | Works | Works | Works |
| Serial devices (Arduino, WLED, steppers) | Works | Works | Works |
| Ableton Link | Works | Not yet | Not yet |
| Gamepads | Works | Not yet | Not yet |
| SpaceMouse | Works | Planned | Planned |
| Trackpad pinch and two-finger pan | — | Works | Works |

On macOS, **Cmd** takes the place of **Ctrl** in all shortcuts, and menus show the Mac key names. The
menu called **TiXL** on Windows and Linux is called **File** on macOS, because the Mac's own app menu
already carries the name TiXL.

## Computer vision and AI

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| MediaPipe (pose, hands, face) | Works | Planned | Planned |
| OpenCV operators (camera calibration, point scanning) | Works | Not yet | Not yet |
| Beat and bar detection from audio | Works | Works | Works |

## Editor and system integration

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| File dialogs, clipboard, drag and drop | Works | Works | Works |
| Reveal in Explorer / File Manager / Finder | Works | Partial | Works |
| Thumbnails in the asset and symbol library | Works | Works | Works |
| Eyedropper outside the TiXL window | Works | Not yet | Not yet |
| Deleted assets go to the trash | Works | Not yet | Not yet |
| Crash reports | Works | Works | Works |

On Linux, **Reveal in File Manager** opens the folder but doesn't select the file. On Linux and macOS,
deleting an asset removes it for good instead of moving it to the trash.

## Installing

| | Windows | Linux | macOS |
|---|---|---|---|
| Package | Installer | Tarball, AUR | App in a DMG (preview) |
| Signed | Yes | — | Not yet |

The macOS app isn't signed with an Apple developer certificate yet. The first time you open it, macOS
blocks it; allow it once under **System Settings → Privacy & Security → Open Anyway** — the steps are in
[Install on macOS](InstallMacOS.md#allow-tixl-to-open). When TiXL first opens your projects, macOS also asks
for permission to access your Documents folder.

Where TiXL keeps its files on each system is described in [Files and folders](FilesAndFolders.md).

## See also

- [Install on Linux](InstallLinux.md)
- [Install on macOS](InstallMacOS.md)
- [System requirements](../getting-started/SystemRequirements.md)
