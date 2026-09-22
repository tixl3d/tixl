# Linux editor: the editor on SDL3 and Vulkan

**Status:** In progress — M1 started 2026-09-22.
**Parent:** [Plan_CrossPlatformV5](Plan_CrossPlatformV5.md) — this is the editor half of its Phase 2 (SDL3) and
all of Phase 5 (Editor on Linux), broken into milestones that can each be verified on their own.
**Branch:** `feat/linux-port`.

## Why now

On 2026-09-21 the Player rendered real exported projects on Arch/RADV through the Vulkan backend:
`SimpleLoadImage` (a 2D layer) and `SimpleRotatingTorus` (a lit, multisampled 3D mesh). Getting there exercised
most of what the editor's own rendering needs — textured quads, scissor, blending, constant buffers, render
targets, MSAA resolve, a swapchain with vsync and resize — and shook out a dozen latent backend bugs.

The editor's ImGui renderer is already on the facade: `WindowsUiContentDrawer` pushes and pops state through
the compat layer, and `GpuTextureView` carries an `ImGuiTextureId`. What stands between the editor and Linux
is mostly the window it runs in, not how it draws.

## Sequencing — decide before starting

The parent plan ships SDL3 on Windows first (Phase 2, v4.x, D3D11, validated bit-exact by the visual suite)
and only then moves the editor to Linux (Phase 5). The branch we are on can take either order:

- **Windows first (parent plan).** Port the editor to SDL3 on `main`, ship it in v4.x, let Windows users find
  the input and DPI regressions, then run it on Linux. Slower to first light on Linux; much safer for v4
  users, and every regression is found against a known-good D3D11 image.
- **Linux first (this branch).** Port the editor to SDL3 here and bring it up on Linux directly. Faster to a
  visible editor, but input and DPI regressions surface on Linux where there is no D3D11 reference to diff
  against, and the SDL3 editor reaches Windows later.

**Decided 2026-09-22: Linux first on this branch, with every SDL3 change kept platform-neutral** — nothing in
the window, input or display code may branch on Linux. The same commits then cherry-pick to `main` for the
Windows-first shipping order the parent plan wants, and the visual suite on Windows still catches regressions.

## Inventory (measured 2026-09-22)

What keeps `Editor.csproj` on `net10.0-windows`, and what fails at runtime on Linux:

| Area | Files | Blocks the Linux build? | Replacement |
|---|---|---|---|
| WinForms window shell — `AppWindow`, `ImGuiDx11RenderForm`, `ProgramWindows`, `MouseWheelPanning`, `IWindowsFormsMessageHandler`, `SplashScreen` | 6 (~1450 lines) | **yes** (WinForms) | SDL3 window, modelled on the Player's `PlayerWindow` |
| `Screen` / `Cursor` in `OutputSetup/*`, `TimeLine`, `ResolutionHandling`, `OutputWindowHandling` | ~10 | **yes** (WinForms) | `IDisplayProvider` (`SdlDisplayProvider`) and `SdlCoreUi`, both already exist |
| SpaceMouse raw input | 2 | yes (WinForms) | platform-gated; non-goal for v5.0 per the parent plan |
| ImGui input from Win32 `WM_*` messages | in `ImGuiDx11RenderForm` | yes | C# port of `imgui_impl_sdl3`; `SdlKeyMap.ToVirtualKey` already maps SDL keycodes to TiXL's keys |
| `SharpDX.D3DCompiler` in `WindowsUiContentDrawer` | 1 | no — runtime | compile through `ShaderCompiler.Instance`, which is Slang on Linux |
| `SharpDX.WIC` in `FontAtlasGenerator` | 1 | no — runtime, **needed for M1** | StbImageSharp / StbImageWriteSharp (Unlicense OR MIT) |
| `SharpDX.WIC` in `ThumbnailManager`, `VideoThumbnails`, `VideoClipThumbnailCache`, `ScreenshotWriter` (+`SharpDX.IO`) | 4 | no — runtime | same StbImage pair; later milestone |
| `SharpDX` in `ThumbnailCanvasRendering` | 1 | check | likely `DataStream`; the facade has one |
| Dialogs and message boxes behind `BlockingWindow` | 24 call sites | **no** | `SilkWindows` (GLFW + OpenGL) already builds and runs on Linux — the Player uses it. Moving it to SDL3 is the parent plan's goal, not a Linux blocker. |

The only hard gate on the Linux **build** is WinForms: 17 files. Everything else builds on `net10.0` and fails,
or is gated, at runtime.

## Milestones

Each ends with something visible and a check that does not depend on anyone watching the screen.

### M1 — an empty ImGui frame on Linux

The editor window opens through SDL3 on Vulkan and draws ImGui's demo window. No project, no graph, no
outputs. This is the editor's equivalent of the Player's first frame: it proves the shell, input, font atlas
and ImGui renderer together, with nothing else in the way.

1. **Done.** SDL3 window and main loop replacing `AppWindow` / `ImGuiDx11RenderForm` / `ProgramWindows`. Frame
   lifecycle exactly as the Player's: `BeginFrame` → acquire the back buffer → draw → `Present` → `EndFrame`,
   acquiring **every frame** (see [Lessons](#lessons-from-the-player)).
2. **Done, untested on a German layout.** `imgui_impl_sdl3` port for mouse, keyboard, text input, cursors and clipboard. German QWERTZ must behave
   as on Windows.
3. **Done** — through `IEditorSystemUiService.AllScreens` (`SdlScreenList`, invalidated on SDL display events).
   `Screen`/`Cursor` call sites onto `IDisplayProvider` / `SdlCoreUi`; SpaceMouse gated off.
4. **Done.** `WindowsUiContentDrawer` compiles through `ShaderCompiler.Instance`.
5. **Done.** `FontAtlasGenerator` onto StbImageSharp.
6. **Done.** `Editor.csproj` to `net10.0` once no WinForms is left. The splash screen draws with SDL's
   renderer; file dialogs are SDL's; the eyedropper and the debug bridge's window capture stay Windows-only
   (GDI), off `System.Drawing.Common`. Display indices now follow SDL's order, which may differ from
   WinForms' `Screen.AllScreens` — check saved output bindings on a multi-monitor Windows machine.

Done when: `TIXL_VULKAN_VALIDATION=1` runs the demo window for a minute of mouse and keyboard use with a
silent validation layer and no device loss, and a frame-count probe shows presentation at the display's
refresh rate rather than thousands of frames per second.

**Measured 2026-09-22** (Radeon 8060S, RADV, Wayland): the editor starts on Vulkan, loads all ten packages and
runs its full UI — not only the demo window — for 60 s with the validation layer silent, at 122 fps on a 120 Hz
display (`getMetrics` twice, 10 s apart). Still open: the minute of mouse and keyboard use, which needs a
person at the machine.

Found on the way, and gated or fixed:
- MIDI (NAudio → `winmm.dll`) scans no devices off Windows.
- The stall overlay presents from a second thread through D3D11's thread-safe immediate context and reads
  Win32 message state; off on Vulkan until it has a design of its own.
- WinForms installed a synchronization context on the main thread, and code relied on it
  (`TaskScheduler.FromCurrentSynchronizationContext` in the OSC listener). `MainThreadSynchronizationContext`
  replaces it, drained once per loop pass.
- `SlangShaderCompiler` dereferenced a null owner for the editor's own shaders.
- Texture creation with initial data sized a 2D subresource by its slice pitch, which D3D11 callers leave 0.

**How the editor frame works today** (mapped 2026-09-22 — read before changing it):

- `Program.Main` sets `EditorUi.Instance = new MsFormsEditor()` (the WinForms-specific UI services — a seam
  to implement for SDL3), `ShaderCompiler.Instance = new DX11ShaderCompiler()` (needs the Player's platform
  choice), and dialogs through `SilkWindowProvider`.
- `ProgramWindows.InitializeMainWindow` enumerates adapters through DXGI and wraps a native SharpDX device in
  `D3D11Backend`. On Linux the whole block becomes `new VulkanBackend(...)`, which picks its own device.
- `AppWindow.RunRenderLoop` hands the loop to SharpDX's `RenderLoop.Run(Form, …)`, which pumps WinForms
  messages. Frame pacing P/Invokes `kernel32.dll` `WaitForSingleObjectEx`; route it through
  `SwapChain.WaitForFrameLatency()` instead, which the Vulkan backend already handles.
- `WindowsUiContentDrawer.RenderCallback` is the frame: `NewFrame` → output windows → the viewer window →
  `Main.PrepareRenderingFrame` → `T3Ui.ProcessFrame` → `ImGui.Render` + `RenderDrawData` → `Present`.
- **It has both bugs the Player had.** Nothing calls `Device.BeginFrame()`/`EndFrame()`, and `AppWindow` keeps
  one `RenderTargetView` for its lifetime instead of acquiring per frame. On Vulkan that renders nothing, at
  thousands of frames per second, with a clean log. Bracket the frame and acquire every frame, as
  `Player/Program.RenderLoop.cs` and `PlayerWindow.AcquireBackBuffer` now do.
- **Several swapchains present in one frame**: the main window, the viewer (`T3Ui.ShowSecondaryRenderWindow`)
  and each output display window. That is the first real use of the per-swapchain acquire semaphores. Keep
  the viewer hidden for M1 and turn it on as the first thing M3 tests.

### M2 — a project opens

The editor loads a project and shows the graph canvas: nodes, connections, parameter windows, the timeline.
No output views yet.

Done when: a project opens, a node can be added, wired and edited, and the project saves and reloads
unchanged.

### M3 — output views

Output windows and in-editor previews: render-to-texture into ImGui images, several targets per frame, and
the editor's constant panel resizing. This is where the editor starts leaning on the swapchain and render
targets harder than the Player ever did.

Done when: `SimpleRotatingTorus` and `SimpleLoadImage` render in an output view and in a second output window
at the same time, survive repeated panel and window resizes, and validation stays silent. That second window
is also the first real test of the per-swapchain acquire semaphores (see [Open questions](#open-questions)).

### M4 — thumbnails, screenshots, readback

The remaining WIC users (`ThumbnailManager`, `VideoThumbnails`, `VideoClipThumbnailCache`,
`ScreenshotWriter`) onto StbImage, and the readback paths they depend on — colour picking, screenshots, the
visual suite's comparisons.

Done when: thumbnails appear in the symbol library and a screenshot round-trips.

Known: the debug bridge's UI `screenshot` hangs the main thread on Vulkan — the readback or the WIC encode
in `ScreenshotWriter` never completes. Start M4 there; it is also what the visual suite needs.

### M5 — the Linux runtime

The parent plan's Phase 5 items: XDG paths, case-sensitive resource resolution, csproj generation and
`dotnet build` for user packages, operator hot reload, BASS on Linux (or a clean audio-off path), and the
debug bridge.

Done when: the parent plan's Phase 5 criteria hold — open projects, edit graphs, hot-reload operators and run
the visual suite on Arch — and the manual test set "Linux Editor (Wayland)" passes on KDE and one other
compositor.

## Lessons from the Player

Rules for working on this, each learned the expensive way while bringing the Player up:

- **Always run with `TIXL_VULKAN_VALIDATION=1`.** The Player ran without the layer for most of a day and
  reported GPU faults as a bare lost device. With the layer on, the next two bugs were named exactly, in two
  runs, with no faults.
- **Reproduce in `Core.Tests` before running the app.** Several bugs faulted the GPU and one hung the desktop.
  The validation-guarded GPU tests turn those into a failed assertion in milliseconds. Every backend bug fixed
  in this plan gets a test first.
- **Split the pipeline with probes instead of theorising.** A distinctive clear colour, a pixel shader forced
  to one colour, and a vertex shader emitting a fixed triangle narrowed a black torus to "zero vertices" in
  three runs, after several wrong theories. Keep probes in a copy of the assets, never the repo.
- **Verify through logs, not screenshots.** Frame-count and state probes in the log; a person looks at the
  screen only to judge what a log cannot.
- **The facade must keep D3D11's semantics, not only its shape.** Each of these was assumed by operators and
  silently broken on Vulkan: views keep their resource alive (reference counting); view descriptions are
  unions (`Buffer` aliases `BufferEx`); the immediate context works outside a frame; typeless formats exist;
  out-of-bounds buffer reads return zero. When the editor turns up the next one, fix it in the facade, not in
  the operator.

## Risks

- **Input fidelity.** Keyboard layouts, IME, dead keys and HiDPI scaling are where a hand-ported
  `imgui_impl_sdl3` goes wrong quietly. The parent plan's manual test set covers them; they need a person at
  the keyboard.
- **Wayland.** No absolute window positioning, fractional scaling, portal-based file dialogs (asynchronous —
  call sites need callbacks), and behaviour that differs between compositors.
- **Render-to-texture volume.** The editor keeps many targets alive and recreates them on every resize, which
  is where the view-lifetime and resize bugs found in the Player concentrate.
- **One Vulkan allocation per resource.** The parent plan's known gap. The editor creates far more resources
  than the Player and may hit the driver's allocation limit before a sub-allocator exists.

## Open questions

- **Vertex attributes bind by position, not by semantic.** `VertexAttribute.Semantic` is documented as
  *"The backend resolves it against the shader's reflection"*, but `VulkanPipelineFactory` sets
  `location = i` from the array index and never reads it. A vertex shader whose input struct is declared in a
  different order from its input layout gets its attributes swapped on Vulkan and works on D3D11, which
  matches semantics. Operators never hit it — they read through `SV_VertexID` — but the ImGui renderer
  uses a vertex buffer, and its input struct was declared `pos, col, uv` against a `pos, uv, col` layout.
  Worked around in M1 by declaring the ImGui inputs in layout order. The real fix: carry each vertex input's
  semantic and location out of Slang's reflection into the `SpirvBlob`, and resolve `Semantic` against it when
  the pipeline is built.
- **Sequencing** — Linux first or Windows first; see [Sequencing](#sequencing--decide-before-starting).
- **Per-swapchain acquire semaphores are unexercised.** Fixed by construction for multiple windows, but every
  run so far had one swapchain. M3's second output window is its first real test.
- **The unused normal output.** `mesh-Draw` writes a normal to a second target that nothing binds — harmless
  now, but the editor's normal-buffer previews may need that target wired up.
- **`SilkWindows` or SDL3 dialogs.** Keeping GLFW for dialogs gets to M1 fastest; the parent plan wants them on
  SDL3. Decide whether that move belongs in this plan or stays in Phase 2.

## Non-goals

As in the parent plan: ImGui multi-viewport, an ImGui upgrade, SpaceMouse, NDI, Spout, Mediapipe, webcams and
screen capture on Linux for v5.0.
