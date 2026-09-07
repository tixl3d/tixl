# TiXL Solution Overview

This document maps the major architectural building blocks in `t3.sln` and provides task-focused entry points for:

- error analysis
- performance analysis
- refactoring
- implementing new UI features

## 1) System Map (What Talks to What)

Core runtime layers:

- `Core/` - shared runtime engine, operator model, resources, animation/audio, serialization contracts
- `Editor/` - authoring UI, graph editing, interaction workflows, and package/project management
- `Player/` - exported playback host and runtime bootstrap for shipped projects

Operator ecosystem:

- `Operators/Lib/` - primary built-in operators
- `Operators/TypeOperators/` - base/type operators
- `Operators/Examples/` - reference usage patterns
- other packages (`Mediapipe`, `Spout`, `Ndi`, `unsplash`, `skills`) - specialized integrations

Shared support services:

- `Logging/` - diagnostics pipeline
- `Serialization/` - serialization helpers
- `SystemUi/` - UI abstraction contracts
- `MsForms/`, `SilkWindows/`, `ImguiWindows/` - platform/window/input/ImGui bridge
- `ProjectUpdater/` - project migration and update support

## 2) Key Dependency Flow

- `Editor` -> `Core`, `Logging`, `MsForms`, `SilkWindows`
- `Player` -> `Core`, `MsForms`, `SilkWindows`
- `Core` -> `Logging`, `Serialization`, `SystemUi`
- `SilkWindows` -> `ImguiWindows`, `Logging`, `Serialization`, `SystemUi`
- `Operators/*` -> usually `Core` (+ `Logging`, sometimes `Serialization` and `SystemUi`)

Practical placement rule:

- runtime behavior/state in `Core`
- editor-only UX/tooling in `Editor`
- standalone playback concerns in `Player`
- content/effect logic in operator packages

## 3) Major Building Blocks by Responsibility

### Runtime Model and Evaluation

- `Core/Operator/` - symbols, instances, children/connections, slots, evaluation context
- `Core/Compilation/` - runtime assembly loading and operator type discovery
- `Core/Model/` - symbol package metadata and project model glue

Start here for:

- operator evaluation bugs
- graph-level behavior mismatches
- refactors of symbol/instance lifecycle

### Resources, Rendering, and Assets

- `Core/Resource/` - resource manager, shader compiling, package resource handling
- `Core/Rendering/` - rendering data structures and utilities
- `Core/DataTypes/` - shared data objects consumed by operators and rendering

Start here for:

- shader/resource loading errors
- GPU resource lifecycle and invalidation issues
- rendering performance and memory pressure

### Editor Application and UI

- `Editor/Program.cs` - editor bootstrap and service initialization
- `Editor/Compilation/ProjectSetup.cs` - project/package lifecycle in the editor
- `Editor/Gui/` - windows, graph UI, interactions, dialogs, styling
- `Editor/UiModel/` - commands/state transitions backing UI actions

Start here for:

- editor startup/load failures
- interaction bugs and UI regressions
- implementing new editor features

### Playback Runtime

- `Player/Program.cs` + partials (`Program.Input.cs`, `Program.Loading.cs`, `Program.RenderLoop.cs`)

Start here for:

- export playback issues
- input handling/runtime startup bugs in standalone projects
- runtime-only performance issues not visible in editor mode

### Operator Packages

- `Operators/Lib/` - primary operator implementations
- `Operators/TypeOperators/` - foundational type-building operators
- `Operators/Examples/` - expected usage and composition references

Start here for:

- feature-level behavior changes in operators
- adding new operators
- validating operator conventions and packaging behavior

## 4) Task-Oriented Navigation

### Error Analysis

1. Reproduce and classify: startup, load/compile, evaluation, rendering, UI interaction, export playback.
2. Check logs and startup flow (`Editor/Program.cs` or `Player/Program.cs`).
3. Trace package/symbol loading (`Editor/Compilation/ProjectSetup.cs`, `Core/Model/`, `Core/Compilation/`).
4. Trace runtime execution paths (`Core/Operator/`, `Core/Resource/`, relevant `Operators/*`).
5. Validate whether issue is editor-only, player-only, or shared core behavior.

### Performance Analysis

Focus first on frame-loop paths:

- editor draw/update code in `Editor/Gui/*`
- operator update/evaluation in `Core/Operator/*` and `Operators/*`
- resource/shader churn in `Core/Resource/*`

Performance guardrails:

- avoid allocations and LINQ in per-frame code
- prefer simple loops and reusable buffers
- isolate one-off user action code from hot paths

### Refactoring

1. Define boundary (Core vs Editor vs Player vs Operators).
2. Preserve contracts across `Symbol`/`Instance`/slot/resource layers.
3. Keep package loading compatibility in mind (`ProjectSetup`, `SymbolPackage`, release metadata).
4. Prefer small, staged changes and verify behavior in both editor and player when relevant.

### Implementing New UI Features

Typical flow:

1. Add state/command model in `Editor/UiModel/` when needed.
2. Add rendering and interaction in the relevant `Editor/Gui/Windows/*` or helper area.
3. Reuse existing UI helpers before creating new primitives.

UI implementation guidelines:

- use `UiColor`/`UiColors` helpers instead of hard-coded `float4` colors
- use fonts sparingly (prefer `Normal` and `Small`)
- prefer `CustomComponents` and `FormInputs` for layout/input widgets

### Registering a file extension / drag-drop on the graph

When you need a new file extension (e.g. `.data`, `.wav`, `.gltf`) to be drag-droppable onto the graph and automatically create a specific operator, the registry lives in two places that work together:

1. **`Editor/Gui/Windows/AssetLib/AssetHandling.cs`** — `InitAssetTypes()` registers each `AssetType` with its extensions and a `PrimaryOperators` array of Symbol GUIDs. `PrimaryOperators[0]` is the default operator created on drop. Adding a `.wav` → `AudioClip` mapping is a few-line edit here (alongside Images, Video, Audio, Obj, Gltf, Shader, JSON, etc.).
2. **The operator's `.t3ui`** — set `"Usage": "FilePath"` on the path-input UI and `"FileFilter": "wav mp3 ogg"` so the editor's file picker filters correctly when the user clicks `…`. Example: `Operators/Lib/io/audio/AudioClip.t3ui`.

What happens automatically on drop:

- `Editor/Gui/MagGraph/Ui/DropHandling.cs` resolves the file's extension via `FileExtensionRegistry` → `AssetType.TryGetForFilePath()` → picks `PrimaryOperators[0]`.
- `FileImport.TryImportDroppedFile()` **copies the file into the active project's `Assets/<subfolder>/`**, creating the subfolder if needed (subfolder name comes from `AssetType.Subfolders[0]`).
- `CreateAssetOperatorOnGraph()` then instantiates the operator at the drop position and writes the project-relative asset path (e.g. `pixtur.Playground:audio/rec-005.wav`) into the operator's path input.

So the contract for a new asset-droppable operator is: pick a `Subfolder` name in `AssetHandling.cs`, add the extension(s), add the operator GUID as `PrimaryOperators[0]`, and set `Usage: FilePath` + `FileFilter` on the path input UI. No drop-handler code to write.

For TimeClip-shaped media operators (e.g. `AudioClip`, `PlayVideoClip`), also set `DirtyFlagTrigger = DirtyFlagTrigger.Animated` on the `TimeClipSlot` output — without it, an unconnected dropped clip won't be evaluated each frame and won't play.

## 5) Code Quality and Review Expectations

When touching code, always:

- point out obvious problems, misleading code, incorrect implementations, and typos
- fix spelling mistakes in touched comments on the fly (keep edits minimal and local)
- keep docs and code comments concise; avoid redundant parameter docs when names are self-explanatory

## 6) Fast Reading Order for New Contributors

1. `README.md`
2. `AGENT_INSTRUCTIONS.md`
3. `Core/Operator/` and `Core/Resource/`
4. `Editor/Program.cs` and `Editor/Compilation/ProjectSetup.cs`
5. `Editor/Gui/` and `Editor/UiModel/`
6. `Operators/Lib/`, `Operators/TypeOperators/`, `Operators/Examples/`

## 7) References

- Main docs: https://github.com/tixl3d/tixl/wiki
- How TiXL works: https://github.com/tixl3d/tixl/wiki/help.HowTixlWorks
- Operator conventions: https://github.com/tixl3d/tixl/wiki/dev.OperatorConventions

