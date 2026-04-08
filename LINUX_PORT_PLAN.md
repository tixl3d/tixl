# Plan: Port Tixl to Linux

## Context

Tixl is a 178k LOC C#/.NET 9.0 real-time motion graphics toolkit that is currently Windows-only. The codebase already shows signs of cross-platform intent (Editor.csproj has `RuntimeIdentifiers` including `linux-x64`, `TixlAssemblyLoadContext` has Linux `.so` loading logic, `SilkWindows` targets `net9.0`), but the core rendering, audio, and windowing stacks are locked to Windows APIs.

The three critical blockers are: (1) all GPU rendering is SharpDX/Direct3D11, (2) audio capture uses WASAPI, and (3) windowing/dialogs use Windows Forms.

---

## Phase 0: Build System & Target Framework (1-2 weeks)

**Goal:** Make the solution structurally compilable on Linux.

### 0.1 Update `Tixl.props` for platform-conditional TFMs
- **File:** `Tixl.props` (line 11)
- Change `TixlNetFrameworkVersion` from hardcoded `net9.0-windows` to conditional:
  ```xml
  <TixlNetFrameworkVersion Condition="$([MSBuild]::IsOSPlatform('Windows'))">net9.0-windows</TixlNetFrameworkVersion>
  <TixlNetFrameworkVersion Condition="!$([MSBuild]::IsOSPlatform('Windows'))">net9.0</TixlNetFrameworkVersion>
  ```
- Define `PLATFORM_WINDOWS` / `PLATFORM_LINUX` constants for `#if` directives

### 0.2 Categorize projects by actual Windows dependency

**Already cross-platform (`net9.0`):** Logging, Serialization, SystemUi, SilkWindows, ImguiWindows, SilkWindowTest

**Genuinely need Windows Forms (exclude on Linux):** MsForms, Windows

**Need SharpDX removed to become cross-platform:** Core, Editor, Player, ProjectUpdater, all Operator packages

**Permanently Windows-only:** Operators/Spout, Operators/Ndi, Operators/Mediapipe

### 0.3 Fix the `TargetLinuxFramework` bug
- **File:** `Editor/Compilation/ProjectXml.cs:272`
- Current: `TargetFramework + "net9.0-linux"` (produces `net9.0net9.0-linux`)
- Fix to: `TargetFramework` (there is no `net9.0-linux` TFM; plain `net9.0` is correct for Linux)

### 0.4 Add conditional package references in Core.csproj
- Gate `ManagedBass.Wasapi` behind Windows condition
- Replace `OpenCvSharp4.Windows` with `OpenCvSharp4.runtime.linux-x64` on Linux
- Gate `System.Management` behind Windows condition

---

## Phase 1: GPU Abstraction Layer (6-10 weeks) -- CRITICAL PATH

**Goal:** Decouple the engine from SharpDX/DX11 and introduce a Veldrid backend for Linux.

### Strategy: Adapter Layer with Type Aliasing

The key constraint is that 924+ operator `.cs` files use SharpDX types via `Using` aliases (defined in `ProjectXml.cs:420-467`). The abstraction must either:
- **(A)** Redefine these types in `T3.Core` namespaces so the aliases redirect transparently, OR
- **(B)** Use Veldrid directly, mapping its types through the same alias names

Option (A) is recommended -- define Tixl-owned wrapper types (e.g., `T3.Core.Gpu.Buffer`, `T3.Core.Gpu.Format`, `T3.Core.Gpu.ShaderResourceView`) that internally delegate to either SharpDX (Windows) or Veldrid (Linux). This lets all 30+ operator Using aliases point to Tixl types instead of SharpDX types, requiring zero changes to operator source code.

### 1.1 Create GPU abstraction types in `Core/Gpu/` (new directory)
Define wrapper types matching the 30 SharpDX aliases from `ProjectXml.cs:420-467`:
- `Buffer`, `ShaderResourceView`, `UnorderedAccessView`, `Format`, `SamplerState`, `BlendState`, etc.
- `IGpuDevice` interface replacing `SharpDX.Direct3D11.Device`
- `IGpuContext` interface replacing `DeviceContext` immediate context usage

### 1.2 Create DX11 backend (Windows, preserves current behavior)
- New project: `GpuBackend.DX11/` targeting `net9.0-windows`
- Implements all abstractions using SharpDX -- essentially the current code relocated
- Windows continues working identically

### 1.3 Create Veldrid backend (Linux)
- New project: `GpuBackend.Veldrid/` targeting `net9.0`
- NuGet: `Veldrid`, `Veldrid.SPIRV`, `Veldrid.StartupUtilities`
- Implements same interfaces using Veldrid's Vulkan backend
- `Veldrid.GraphicsDevice.CreateVulkan()` on Linux

### 1.4 Redirect ResourceManager to use abstraction
- **File:** `Core/Resource/ResourceManager.Graphics.cs` -- replace `SharpDX.Direct3D11.Device` with `IGpuDevice`
- **File:** `Core/Resource/DefaultRenderingStates.cs` -- replace SharpDX state objects
- **File:** `Core/DataTypes/BufferWithViews.cs` -- replace `SharpDX.Direct3D11.Buffer` with abstraction `Buffer`

### 1.5 Update ProjectXml.cs code generation
- **File:** `Editor/Compilation/ProjectXml.cs:340-348` -- replace SharpDX.dll references with abstraction assembly references
- **File:** `Editor/Compilation/ProjectXml.cs:420-467` -- redirect Using aliases from `SharpDX.*` to `T3.Core.Gpu.*`

### 1.6 Update all operator csproj Using aliases
- 8 operator csproj files contain ~30 SharpDX Using aliases each
- Mechanical replacement: `SharpDX.Direct3D11.Buffer` -> `T3.Core.Gpu.Buffer`, etc.

---

## Phase 2: Shader Cross-Compilation (3-5 weeks)

**Goal:** Compile the 424 HLSL shaders for Vulkan via SPIR-V.

### Strategy: Keep HLSL, cross-compile with `dxc -spirv`

Microsoft's `dxc` (DirectXShaderCompiler) compiles HLSL to SPIR-V directly and runs on Linux. This avoids manually rewriting 424 shader files.

### 2.1 Create `SpirVShaderCompiler` subclass
- **Base class:** `Core/Resource/ShaderCompiling/ShaderCompiler.cs` (already abstract)
- **Existing:** `Core/Resource/ShaderCompiling/DX11ShaderCompiler.cs` (Windows)
- **New:** `Core/Resource/ShaderCompiling/SpirVShaderCompiler.cs` (Linux)
- Use NuGet `Microsoft.Direct3D.DXC` or invoke `dxc` binary with `-spirv` flag
- Targets SM 6.0 (`vs_6_0`, `ps_6_0`, `cs_6_0`, `gs_6_0`)

### 2.2 Batch-test all 424 shaders
- Automate `dxc -spirv` compilation of all shaders in `Operators/Lib/Assets/shaders/` and `EditorResources/shaders/`
- Triage failures: SM5-specific intrinsics, DX-only semantics, register binding differences
- Fix HLSL source where needed (expect ~5-15% needing minor tweaks)

### 2.3 Shader caching
- **File:** `Core/Resource/ShaderCompiling/ShaderCompiler.Caching.cs`
- Existing cache infrastructure works for SPIR-V blobs unchanged
- Add a cache subdirectory to distinguish DX11 bytecode from SPIR-V

---

## Phase 3: Audio Platform Abstraction (1-2 weeks)

**Goal:** Audio playback and capture on Linux.

### 3.1 BASS library is already cross-platform
- `ManagedBass` NuGet works on Linux; `libbass.so` available from un4seen.com
- **File:** `Core/Audio/AudioMixerManager.cs:232` -- change `"bassflac.dll"` to `"libbassflac.so"` on Linux
- **File:** `Core/Audio/AudioMixerManager.cs:631` -- platform-conditional native lib path

### 3.2 Replace WASAPI capture with BASS recording API
- **File:** `Core/Audio/WasapiAudioInput.cs` -- Windows-only, keep as-is
- **New:** `Core/Audio/LinuxAudioInput.cs` -- uses `Bass.RecordInit()` / `Bass.RecordStart()` (works via ALSA/PulseAudio/PipeWire)
- Extract interface from both, select at runtime via platform check

### 3.3 Add Linux native BASS libraries
- Create `Dependencies/linux-x64/` with `libbass.so`, `libbassmix.so`, `libbassflac.so`
- Add MSBuild copy targets conditioned on RID

---

## Phase 4: Window Management & UI (2-3 weeks)

**Goal:** Replace Windows Forms with cross-platform alternatives.

### 4.1 Create `LinuxUi/` project implementing SystemUi interfaces
The `SystemUi/` project already defines clean interfaces (`ICoreSystemUiService`, `IEditorSystemUiService`, `IFilePicker`, `ICursor`, `IScreen`). Create Linux implementations:

| Interface method | Linux implementation |
|---|---|
| `OpenWithDefaultApplication(uri)` | `Process.Start("xdg-open", uri)` |
| `ExitApplication()` | `Environment.Exit(0)` |
| `SetClipboardText` / `GetClipboardText` | Silk.NET clipboard or `xclip` |
| `CreateFilePicker()` | GTK dialog via P/Invoke, or `zenity`/`kdialog` |
| `AllScreens` | Silk.NET monitor enumeration |
| `EnableDpiAwareScaling()` | No-op (compositor handles DPI) |

### 4.2 Replace RenderForm with Silk.NET windowing
- **Files:** `Editor/App/AppWindow.cs`, `Editor/App/ImGuiDx11RenderForm.cs`, `Editor/App/ProgramWindows.cs`
- These use `SharpDX.Windows.RenderForm` for the main window
- On Linux: use Silk.NET `IWindow` (already proven by `SilkWindows/`)
- The main content rendering surface becomes a Veldrid swapchain on a Silk.NET window

### 4.3 Replace XInput with cross-platform gamepad input
- SharpDX.XInput is Windows-only
- Silk.NET.Input already provides cross-platform gamepad support via SDL/GLFW

### 4.4 Handle serial port enumeration
- **File:** `Core/IO/SerialConnectionManager.cs` -- uses `System.Management` (WMI) on Windows
- On Linux: enumerate `/dev/ttyS*` and `/dev/ttyUSB*`

---

## Phase 5: Operator Package Portability (2-3 weeks)

**Goal:** Operator packages build on Linux; Windows-only operators gracefully excluded.

### 5.1 Gate Windows-only operator packages
- Exclude Spout, NDI, Mediapipe from Linux builds via platform conditions in `Editor.csproj:149-159`
- Ensure the editor handles missing packages gracefully (load errors -> warnings, not crashes)

### 5.2 Handle video operators
- `PlayVideo.cs`, `PlayVideoClip.cs` use `SharpDX.MediaFoundation`
- `VideoDeviceInput.cs` uses `DirectShowLib`
- Short-term: gate behind `#if PLATFORM_WINDOWS` with stubs on Linux
- Long-term: implement FFmpeg-based decode via `FFmpeg.AutoGen` NuGet

### 5.3 Update all operator csproj files
- Change target frameworks from `net9.0-windows` to the conditional property
- Replace SharpDX package references with abstraction project references

---

## Phase 6: Entry Point Refactoring (1-2 weeks)

**Goal:** Editor and Player boot on Linux.

### 6.1 Platform-conditional initialization in Editor/Program.cs
- Line 90: `new MsFormsEditor()` -> `new LinuxEditorUi()` on Linux
- Line 98: `new DX11ShaderCompiler()` -> `new SpirVShaderCompiler()` on Linux
- Line 161: `ProgramWindows.InitializeMainWindow()` -> Veldrid + Silk.NET initialization on Linux

### 6.2 Platform-conditional initialization in Player/Program.cs
- Lines 131-167: Replace `RenderForm` + DX11 device with Veldrid + Silk.NET window on Linux

### 6.3 ImGui rendering backend
- **File:** `Editor/UiContentDrawing/WindowsUiContentDrawer.cs` -- renders ImGui via DX11
- On Linux: ImGui already renders via OpenGL in `SilkWindows/` -- reuse that path
- Alternatively: use Veldrid's ImGui integration

---

## Critical Path & Parallelization

```
Phase 0 (build system)
    |
    v
Phase 1 (GPU abstraction) -----> Phase 2 (shaders) -----> Phase 6 (entry points)
    |                                                           ^
    +--- Phase 3 (audio) [parallel] ---------------------------+
    |                                                           |
    +--- Phase 4 (windowing) [parallel] -----------------------+
    |                                                           |
    +--- Phase 5 (operators) [parallel] -----------------------+
```

**Phases 3, 4, 5 can proceed in parallel** once Phase 0 is done. Phase 6 is the integration point.

## Total Estimate: 16-27 weeks (1-2 developers)

The critical path is Phase 0 -> Phase 1 -> Phase 2 -> Phase 6 (~12-19 weeks). Phases 3-5 add ~5-8 weeks but can overlap.

## Key Files Reference

| File | Current Dependency | Linux Replacement |
|---|---|---|
| `Tixl.props` | `net9.0-windows` | Conditional TFM |
| `Core/Resource/ResourceManager.Graphics.cs` | `SharpDX.Direct3D11.Device` | `IGpuDevice` abstraction |
| `Core/Resource/ShaderCompiling/DX11ShaderCompiler.cs` | SharpDX.D3DCompiler | SpirVShaderCompiler (dxc) |
| `Core/Audio/WasapiAudioInput.cs` | ManagedBass.Wasapi | ManagedBass recording API |
| `Core/Audio/AudioMixerManager.cs` | Windows DLL paths | Platform-conditional native lib names |
| `Core/DataTypes/Texture.cs` | SharpDX.Direct3D11.Texture2D | Veldrid.Texture |
| `Core/DataTypes/Shader.cs` | SharpDX.Direct3D11.*Shader | Veldrid.Shader |
| `Core/DataTypes/BufferWithViews.cs` | SharpDX.Direct3D11.Buffer/SRV/UAV | Veldrid.DeviceBuffer/ResourceSet |
| `MsForms/` | System.Windows.Forms | New LinuxUi/ project |
| `Editor/App/ProgramWindows.cs` | SharpDX Factory1, Device, SwapChain | Veldrid + Silk.NET |
| `Editor/App/AppWindow.cs` | RenderForm, SwapChain | Silk.NET IWindow |
| `Editor/Compilation/ProjectXml.cs` | Hardcoded SharpDX references/usings | Abstraction type references |
| `Player/Program.cs` | RenderForm, DX11 device, SwapChain | Veldrid + Silk.NET |
| `Dependencies/` | Windows DLLs only | Add linux-x64/ with .so files |

## Verification

1. **Phase 0:** `dotnet build` succeeds on Linux (with stub/empty GPU backend)
2. **Phase 1:** Unit tests for abstraction layer; existing DX11 tests pass on Windows
3. **Phase 2:** Batch compile all 424 HLSL files with `dxc -spirv`; 0 failures
4. **Phase 3:** Audio playback test on Linux with BASS
5. **Phase 6:** Editor launches on Linux, displays ImGui UI, renders a simple operator graph
6. **End-to-end:** Run the Examples operator package on Linux and compare output to Windows reference renders
