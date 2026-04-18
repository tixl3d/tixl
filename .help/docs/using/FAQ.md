# FAQ

## I followed the installation instructions, but TiXL still won't start. What can I do?

Installing the Visual C++ Redistributable often helps: [vc_redist.x64](https://aka.ms/vs/17/release/vc_redist.x64.exe) (from the [Microsoft docs](https://learn.microsoft.com/en-US/cpp/windows/latest-supported-vc-redist?view=msvc-170)).

## What's the difference to Touch Designer?

Both TiXL and Touch Designer use a [directed acyclic graph](https://en.wikipedia.org/wiki/Directed_acyclic_graph) — similar in spirit to Houdini, Maya, and Grasshopper. Information flows from inputs to outputs and both rely heavily on texture-based visualization.

What sets TiXL apart is its focus on keyframe animation and time manipulation, live coding with HLSL shaders and C#, and — thanks to its [demoscene](https://en.wikipedia.org/wiki/Demoscene) roots — small-executable export.

Touch Designer is a mature commercial product with a large community, extensive documentation, and professional support. Some concepts overlap, but underneath the tools diverge significantly.

## Where are the Mac and Linux versions?

TiXL is built with ImGui and .NET, both of which run on Linux and macOS. The rendering engine, however, is DirectX 11 — a full port to Vulkan (with a Metal translation layer on macOS) is a large undertaking and not yet complete.

In the meantime:

- On Linux, see [Install on Linux](../install/InstallLinux.md).
- On macOS, see [Install on macOS](../install/InstallMacOS.md).

If you have a background in .NET and Vulkan and want to help, get in touch.

## Is there a standalone version?

Yes. Download the latest release from the [GitHub releases page](https://github.com/tixl3d/tixl/releases/). The installer bundles the required dependencies (including the .NET runtime). See [Installation](../install/Installation.md).

## How do I update TiXL?

TiXL aims to stay backward-compatible when updating. If you follow a few simple rules, `git pull` on the main branch should "just work":

- Never modify symbols in the `lib.*` namespace without submitting a pull request.
- Don't work directly in the `[Dashboard]` operator — create your own playground or project operators instead.
- Use a consistent namespace for your operators, such as `user.yourname.project`.
- Keep your personal resource files in their own folders, e.g. `Resources/user/yourname/projectTitle/`.

If you're using a standalone release, you need to copy the following to the new version:

- Everything in `.t3/`
- Your `Resources/`
- Your custom operators

Future releases will streamline this migration.

## How do I export an executable?

See [Exporting executables](ExportExecutables.md).

## How do I export an image sequence or video?

See [Exporting videos](ExportVideos.md).

## TiXL doesn't start

TiXL writes a backup of all operators every three minutes. See [Using backups](Backups.md) for how to restore one.

Common causes of startup problems:

- A crash during saving produced incomplete or empty files under `Operators/Types/`.
- The internal state of an operator was corrupted and saved.
