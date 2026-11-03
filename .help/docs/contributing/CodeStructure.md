# Code structure

This page maps the TiXL repository: which folder holds what, and where to look for a given task. It is a starting point for finding your way around, not a full architecture description. For more depth, read [`SOLUTION_OVERVIEW.md`](https://github.com/tixl3d/tixl/blob/main/.agentic/SOLUTION_OVERVIEW.md).

Everything lives in one repository, [tixl3d/tixl](https://github.com/tixl3d/tixl), with a single solution file, `t3.sln`. To get it running, follow [Set up a development environment](../install/InstallDev.md).

## The big picture

TiXL has three layers:

- **`Core/`** is the runtime: the operator model, evaluation, resources, rendering, animation and audio. The editor, the player and every operator package use it.
- **`Editor/`** is the authoring application: the graph, parameter window, timeline, project hub, and all other user interface.
- **`Operators/`** contains the content: packages of operators that are compiled and loaded at runtime, just like your own projects.

`Player/` runs exported projects without the editor. The remaining projects are support libraries that these four use.

Dependencies only point downward: `Editor` and `Player` use `Core`, and operators use `Core`. `Core` never depends on the editor. Put a change in the narrowest place that works: runtime behavior in `Core`, editor-only tooling in `Editor`, effects and content in an operator package.

## Top-level folders

| Folder | Contents |
| --- | --- |
| `Core/` | Runtime engine shared by editor, player and operators |
| `Editor/` | The TiXL editor application |
| `Player/` | Playback host for exported projects |
| `Operators/` | Operator packages: `Lib`, `TypeOperators`, `Examples`, `Skills` and integrations |
| `Logging/` | Logging and diagnostics |
| `Serialization/` | JSON serialization helpers |
| `SystemUi/` | Platform-neutral interfaces for dialogs, windows and clipboard |
| `MsForms/`, `SilkWindows/`, `ImguiWindows/` | Window creation, input and the ImGui bridge |
| `IoServices/` | MIDI, OSC, game pads and other live I/O |
| `VideoServices/` | FFmpeg-based video decoding and export |
| `ProjectUpdater/` | Converts projects and operators from older versions |
| `EditorResources/` | Fonts, icons and shaders used by the editor UI |
| `Dependencies/`, `Vendor/` | Prebuilt third-party libraries |
| `Installer/` | Installer and packaging scripts per platform |
| `Core.Tests/`, `VideoServices.Tests/`, `Tests/` | Unit tests and editor integration tests |
| `Spikes/` | Small throw-away experiments. Not part of the product |
| `.Defaults/` | Default content copied into new installations: project template, layouts, themes, the Skill Quest map |
| `.help/` | User documentation, embedded help, release notes and the help index. See [Documentation structure](DocumentationStructure.md) |
| `.tests-manual/` | Manual test sets for releases |
| `.agentic/` | Instructions and plans for coding agents |
| `.github/` | CI workflows, build actions and issue templates |

The Linux and macOS port replaces the DirectX-only windowing with a graphics abstraction. On that branch you will also find `Graphics/` (the backend-neutral GPU interface), `Graphics.D3D11/` and `Graphics.Vulkan/` (the two backends), `Graphics.Compat/` (compatibility for older operator code), `SdlPlatform/` (windows and input through SDL) and `Native/` (native audio code).

## Core

| Folder | What you'll find |
| --- | --- |
| `Core/Operator/` | `Symbol`, `Instance`, slots, connections, `EvaluationContext`: the operator model and its evaluation |
| `Core/Compilation/` | Loading operator assemblies and discovering operator types |
| `Core/Model/` | Symbol packages and project metadata |
| `Core/Resource/` | Resource manager, shader compilation, assets and package resources |
| `Core/Rendering/`, `Core/DataTypes/` | Rendering helpers and the data types that flow between operators (points, meshes, textures, …) |
| `Core/Animation/` | Curves, keyframes and the animator |
| `Core/Audio/` | Audio playback, analysis and beat timing |
| `Core/IO/`, `Core/Video/`, `Core/Output/` | Input devices, video and output handling |
| `Core/Settings/` | Settings shared by editor and player |

## Editor

| Folder | What you'll find |
| --- | --- |
| `Editor/Program.cs` | Startup |
| `Editor/Compilation/` | Loading and recompiling projects and packages in the editor |
| `Editor/UiModel/` | The editor's model of symbols and projects, and `Commands/` for every undoable change |
| `Editor/Gui/MagGraph/` | The graph editor (the "magnetic graph"): model, interaction, drawing |
| `Editor/Gui/Windows/` | Parameter window, output window, timeline, settings, render export, asset library and other windows |
| `Editor/Gui/Hub/` | Project hub and Skill Quest panel |
| `Editor/Gui/Interaction/` | Shared interaction: keyboard actions, snapping, transform gizmos, MIDI control |
| `Editor/Gui/InputUi/`, `Editor/Gui/OutputUi/`, `Editor/Gui/OpUis/` | How parameters, outputs and special operators are drawn |
| `Editor/Gui/Styling/` | Colors, fonts, icons and shared widgets (`CustomComponents`) |
| `Editor/Gui/Help/` | Operator help, embedded help, release notes and video links |
| `Editor/Gui/Graph/Legacy/` | The previous graph editor. Kept for compatibility; not a reference for new code |
| `Editor/Skills/` | Skill Quest runtime |
| `Editor/Migrations/` | Upgrading projects from older TiXL versions |

## Operators

Each folder under `Operators/` is a package, laid out the same way as a user project:

- `Symbols/` holds the operators, each as a `.cs` file (code), a `.t3` file (the graph) and a `.t3ui` file (layout, descriptions, parameter settings). Subfolders follow the namespace.
- `Assets/` holds shaders, images, meshes and other files the operators load.
- `Utils/` holds helper classes shared by several operators.

| Package | Contents |
| --- | --- |
| `Operators/Lib/` | The operator library. Namespaces in `Symbols/`: `image`, `point`, `particle`, `mesh`, `render`, `field`, `numbers`, `string`, `flow`, `data`, `io`, `geometry` |
| `Operators/Lib/Assets/shaders/` | HLSL shaders for the library, grouped by area (`img`, `points`, `particles`, `sdf`, …) |
| `Operators/TypeOperators/` | Basic operators for TiXL's own data types |
| `Operators/Examples/` | Examples for library operators (`Symbols/Lib/`), HowTos, templates and tutorials |
| `Operators/Skills/` | Skill Quest levels |
| `Operators/Io/`, `Operators/Video/`, `Operators/Ndi/`, `Operators/Spout/`, `Operators/Mediapipe/`, `Operators/Unsplash/` | Integrations with external devices, protocols and services |

## Where to look for…

| Task | Start in |
| --- | --- |
| An operator gives wrong results | Its `.cs` in `Operators/Lib/Symbols/…`, then its shader in `Operators/Lib/Assets/shaders/` |
| Improving an operator description | The operator's `.t3ui`, or edit it in the editor's help panel |
| A new example | `Operators/Examples/Symbols/Lib/`, mirroring the operator's namespace |
| A crash on startup or project load | `Editor/Program.cs`, `Editor/Compilation/ProjectSetup.cs`, `Core/Compilation/` |
| Graph editing behaves strangely | `Editor/Gui/MagGraph/Interaction/` |
| A parameter is drawn wrongly | `Editor/Gui/InputUi/` and `Editor/Gui/Windows/ParameterWindow.cs` |
| Something can't be undone | `Editor/UiModel/Commands/` |
| Timeline or keyframes | `Editor/Gui/Windows/TimeLine/`, `Core/Animation/` |
| Shader compilation errors | `Core/Resource/ShaderCompiling/` |
| MIDI, OSC or other input | `IoServices/`, `Operators/Lib/Symbols/io/` |
| Video playback or export | `VideoServices/`, `Editor/Gui/Windows/RenderExport/` |
| Exported executable misbehaves | `Player/` |
| Opening an old project fails | `Editor/Migrations/`, `ProjectUpdater/` |
| Colors, fonts or icons | `Editor/Gui/Styling/` |

## Where your own projects are

Your projects are not part of the repository. They live in your documents folder, and each project is its own operator package with the same `Symbols/` and `Assets/` layout. See [Files and folders](../install/FilesAndFolders.md).

## See also

- [Set up a development environment](../install/InstallDev.md)
- [Style conventions](StyleConventions.md)
- [Writing C# operators](../advanced/WritingCodeOps.md)
