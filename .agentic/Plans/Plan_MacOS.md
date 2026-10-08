# TiXL on macOS (Apple Silicon, Vulkan via MoltenVK)

**Status:** Draft — 2026-10-07. Nothing implemented yet.
**Scope:** Editor and Player on Apple Silicon Macs (`osx-arm64`), Vulkan through MoltenVK (or LunarG's
KosmicKrisp as a fallback ICD). Intel Macs are out of scope.
**Builds on:** [Plan_CrossPlatformV5](Plan_CrossPlatformV5.md) (Vulkan backend, Slang, SDL3) and the Linux
port on `feat/linux-port`, which already runs the Editor on Linux and, behind a switch, on Windows.

## Why now is cheaper than it looked

The CrossPlatform plan estimated macOS at 40–120 agent hours *after* v5.0. Most of the expensive parts already
exist and are already platform-neutral:

- Rendering is Vulkan with SPIR-V from `slangc`; MoltenVK translates both to Metal.
- Windows, input, clipboard, file dialogs, HiDPI and URL opening go through SDL3 (`SdlPlatform/`), whose NuGet
  package ships `osx-arm64` natives. ImGui.NET ships `libcimgui.dylib`.
- `TixlAssemblyLoadContext` already probes `osx-arm64` runtimes and `.dylib` files; `FileLocations` already maps
  the cache to `~/Library/Caches`; `SlangInstaller` already knows the `macos-aarch64` archive.
- Operator compilation is `dotnet build`, which works on macOS unchanged.

What is left is mostly Vulkan portability details, native-library packaging and macOS conventions (Cmd key,
app bundle, signing).

## Known blockers (from a code survey, 2026-10-07)

### Vulkan device creation — `Graphics.Vulkan/VulkanBackend.cs`

| Issue | Where | Fix |
|---|---|---|
| No portability enumeration: the standard loader hides MoltenVK unless the instance opts in | instance creation (~:66–79) | Add `VK_KHR_portability_enumeration` + `VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR` when the extension is available |
| `VK_KHR_portability_subset` not enabled; the spec requires it when the device advertises it | device extensions (~:121) | Enable when present; query `VkPhysicalDevicePortabilitySubsetFeaturesKHR` and log the gaps |
| `geometryShader = true` unconditionally; Metal has no geometry shaders → `vkCreateDevice` fails | ~:166 | Enable only when supported. Only `RenderToCubemap` / `TextureToCubemap` (PBR environment via `SetEnvironment`) need it — the CrossPlatform plan already wants them rewritten without GS; do that rewrite (layered rendering via `shaderOutputLayer`, or 6 passes) |
| `PickPhysicalDevice` requires API 1.3 + dynamicRendering, synchronization2, scalarBlockLayout, shaderDrawParameters, push_descriptor | ~:1385–1418 | Recent MoltenVK reports 1.3 and these features — **verify with `vulkaninfo` on the target Mac** before writing code. If a feature is missing, log exactly which one (the startup error is the first thing a Mac user sees) |
| `D24UnormS8Uint` not supported on Apple GPUs | `VulkanConvert.cs:50,52` | Format-property query + fallback to `D32S8`, already planned for AMD |
| `R32G32B32_*` not valid as a texture format on Metal (fine as vertex format) | `VulkanConvert.cs:19–21` | Fail that texture creation cleanly (null output), or promote to RGBA32 |
| Storage images: Metal supports read-write access only for a subset of formats | compute ops writing RWTexture | Check `VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT` per format; report per op in the visual suite |
| Tessellation state in the pipeline factory | `VulkanPipelineFactory.cs:98,214` | Never set when unsupported (TiXL has no tessellation shaders) |

Expected visual differences to watch: derivative precision, `robustBufferAccess` semantics (MoltenVK
emulates it), and timestamp queries (`GpuMeasure`). The visual suite on the Mac is the oracle, same as Linux.

### Native libraries and packages

| Dependency | Linux today | macOS needs |
|---|---|---|
| Vulkan loader | `libvulkan.so.1` (system) | `libvulkan.1.dylib` + `libMoltenVK.dylib` — bundled in the app, found via the Vortice loader; check its search names |
| BASS (+mix, flac) | `Dependencies/linux-x64/*.so`, `fetch-bass.sh` | `Dependencies/osx/*.dylib` (un4seen ships universal dylibs); `AudioMixerManager.cs:223,690,694` hardcodes `.so` |
| SkiaSharp | `SkiaSharp.NativeAssets.Linux` for every non-Windows host (`Operators/Lib/Lib.csproj:143`) | Condition on OS: `SkiaSharp.NativeAssets.macOS` |
| FFmpeg | TqkLibrary linux-x64 package (`Operators/Video/Video.csproj:118,177`) | No NuGet with osx-arm64 found. Options: Homebrew `ffmpeg` (dev only), or ship our own dylibs matching FFmpeg.AutoGen's major version. Stretch goal, like on Linux |
| MIDI | ALSA (`IoServices/Midi/MidiDeviceProvider.cs:46`) | CoreMIDI provider (small P/Invoke into CoreMIDI.framework), or RtMidi per Plan_MidiControllerAbstraction |
| Audio input | BASS recording (WASAPI on Windows) | BASS recording via CoreAudio; loopback needs a virtual device (BlackHole) — document, don't build |
| slangc | pinned `~/.local/opt/slang-<ver>` or `TIXL_SLANGC` | Same, plus `xattr -d com.apple.quarantine` after download, or ship it inside the bundle |
| Dependency copy rules | `Editor.csproj:102,112`, `Player.csproj:37,45` copy `linux-x64/*.so` for any non-Windows host | Split by OS: `IsOSPlatform('OSX')` → `Dependencies/osx/*.dylib` |
| RIDs | `Editor.csproj:19` lists `osx-x64` | Replace with `osx-arm64` |

`TixlAssemblyLoadContext.cs:600–603` rewrites native extensions to `.so` on OSX too — check that path.

### OS branches that fall into the Linux `else`

Every "not Windows" branch was written assuming Linux. Each needs a look; most are fine:

- Must change: `AudioMixerManager` (lib names), `DependencyCheck.cs:151,201` (Vulkan loader name, distro
  package hints from `/etc/os-release` → Homebrew / Vulkan SDK hints), `EnvironmentReport.cs` (reads
  `/etc/os-release`, XDG vars → `sw_vers`, GPU name from Vulkan), `MidiDeviceProvider`, `SlangShaderCompiler.cs:53`.
- Probably fine: `ProgramWindows.cs:167`, `Player/Program.cs:541,565`, `SdlCoreUi.cs:41` (reveal-in-folder →
  `open -R`), `SdlPointerGestureSource.cs:17`, `AboutDialog`, `ColorEditPopup`, `SerialConnectionManager`,
  `AssetLibrary.DeleteAssets`, `EditorRestart` (shell-execute restart inside a bundle).
- Still unguarded Windows code that Linux tolerates by luck or try/catch: SharpDX.WIC in `ThumbnailManager`,
  `VideoClipThumbnailCache`, `Help/VideoThumbnails`, `Core/DataTypes/Texture.cs`; SharpDX.XInput; System.Management.
  Fixing these helps both ports.

### macOS conventions (not needed on Linux)

1. **Cmd vs Ctrl.** Mac users expect Cmd+C/V/Z/S. Map Cmd to TiXL's Ctrl modifier at the SDL key-map layer
   (`SdlKeyMap`), keep physical Ctrl available as a second modifier, and render shortcuts as ⌘ in menus and
   tooltips. Decide once whether Ctrl+click means right-click. This is the largest UX item.
2. **Trackpad.** Pinch/two-finger pan already exist for Linux (`SdlPointerGestureSource`); verify on the Mac
   trackpad, momentum scrolling in particular.
3. **Retina.** SDL reports pixel density 2.0; check font atlas size, thumbnails and output windows.
4. **Settings location.** `SpecialFolder.ApplicationData` is `~/.config` on .NET/macOS. Decide: keep it (same
   as Linux, simpler support), or move to `~/Library/Application Support/TiXL`. Recommendation: Application
   Support, decided before the first public Mac build so nobody has to migrate.
5. **Documents access.** Projects live in `~/Documents`; macOS asks for privacy (TCC) permission once. Fine,
   but mention it in the install doc.
6. **App menu / quit.** Cmd+Q and the red close button must go through the normal unsaved-changes path.
7. **Window occlusion.** CAMetalLayer can block `vkAcquireNextImageKHR` while a window is minimized or fully
   covered — same class of problem as Wayland's hidden windows. The render loop must not wait on it.

## Phases

### M0 — Dev setup and first build (0.5–1 day, mostly you)
See [Developer setup on a Mac](#developer-setup-on-a-mac). Done when `dotnet build t3.sln
-p:EnableWindowsTargeting=true` succeeds and `Core.Tests` pass on the Mac.

### M1 — First light (agent work on Linux, verified on the Mac)

**Progress 2026-10-07:** the Editor starts, loads all projects and is interactive on the M4. Done: loader lookup
by full path (`VulkanLoader`, also handed to SDL via `SDL_HINT_VULKAN_LIBRARY` — a bare name misses
`/opt/homebrew/lib`), portability enumeration + subset (subset features enabled as reported, gaps logged:
`pointPolygons`, `samplerMipLodBias`), mip LOD bias dropped where unsupported, optional `geometryShader`,
D24S8 → D32S8 fallback, validation layer that fails to load no longer costs the device, OS-split BASS copy
rules (`Dependencies/osx/`), `osx-arm64` RID, `.dylib` in the load context and BASS names, Mac install hints in
`DependencyCheck`. The 23 GPU tests in Core.Tests used to skip silently on the Mac; they now run and pass.
Homebrew's validation layer manifest names a bare dylib — run with
`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib:/usr/local/lib:/usr/lib` to get validation. **M1 done:**
a new project renders `MandelbrotFractal` correctly with validation clean, and operator projects compile from
the Editor. Known gap carried into M2: cubemap operators (no geometry shaders).
Portability enumeration/subset, conditional geometry shader + tessellation, depth-format fallback, OS-aware
dependency copying, `osx-arm64` RID, Skia macOS natives, BASS dylibs, `DependencyCheck` Mac branch.
Done when the Editor opens a project and renders `MandelbrotFractal` with validation layers clean.
Estimate: 10–25 agent hours. Most of it can be written and compile-checked on Linux; every step needs a
run on the Mac.

### M2 — Editor usable daily
Cmd mapping, trackpad, Retina, settings location, reveal-in-Finder, occlusion handling, CoreMIDI, cubemap
rewrite without GS. Run the visual suite and triage failures by cause (format support, precision, MoltenVK
bugs). Estimate: 20–50 agent hours plus your review/testing time.

**Todo (later):**

- [ ] **Measure the descriptor-set path on MoltenVK.** MoltenVK 1.4.2 returns 0 from `OpArrayLength` for push
      descriptors (fixed upstream in MoltenVK PR #2827), so on portability devices `VulkanCommandList` allocates
      a per-frame descriptor set per draw/dispatch instead of pushing (`VulkanBackend.UsesPushDescriptors`).
      Expected to cost about the same as MoltenVK's own push emulation, but unmeasured. Compare frame times on a
      heavy example project via the debug bridge against forcing push descriptors back on (images will be
      wrong, CPU cost comparable). Once a MoltenVK release with the fix lands in Homebrew / the Vulkan SDK,
      gate the fallback on the driver version instead.
- [ ] **Remove the per-draw LINQ allocation in `VulkanCommandList.PushDescriptors`** (all platforms, not
      Mac-specific). `_pipeline.Declared.GroupBy(...)` and `ToArray()` allocate on every draw and dispatch.
      Precompute the per-set binding lists once when the pipeline is created (`VulkanPipeline`) and iterate
      those.
- [ ] **One failing operator crashes the editor** (all platforms). An exception from an operator's `Update`
      (seen: `LoadSvg` with a missing native library) propagates through `OutputUi.Recompute` and the output
      window into the main loop and aborts the process. Catch per output evaluation, report it on the operator,
      and keep the frame going.
- [ ] **Line-only OBJ files fail to load** (code is platform-neutral; not yet checked on Linux).
      `ObjMesh.TryLoadFromFile` (`Core/Rendering/ObjMesh.cs`) returns `mesh.DistinctDistinctVertices.Count != 0`,
      which is 0 for a file with vertices and `l` entries but no faces. `ThereDemo`'s
      `linedrawings/intro-logo-marion.obj` parses to 2217 positions and 1951 lines and is still rejected
      ("Can't read file" from `LoadObjAsPoints` / `LoadObj`). Also accept positions plus lines.
- [ ] **Visual suite on the Mac: 71 / 97 (2026-10-08).** Fixed so far: 8x MSAA clamp for pipelines, `level`
      shadowing Metal's `level()` (mesh-DrawUnlit, RgbTV, waveform-cs), compute bindings leaking into draw
      transitions, TextureToCubemap and RenderToCubemap (specular prefilter) without geometry-shader fan-out -
      no pipeline fails to build any more. Still failing, by cause:
      - `PbrTests/Basic` (0.03) and `PointShading` (0.01): environment and prefilter now work, but the blue
        channel seems lost - teal and purple surfaces come out green. Not the specular cube map (RGBA16F) nor
        the BRDF LUT format (RGBA16 UNorm storage works); probe the material/albedo path via the bridge.
      - `DemoWorksForEverybody` (14 frames, flat/green CRT frames), `Particles`, `FieldParticleVolume`
        (particles missing), `TestImage2dSDF` (text "S!" missing), `ComplexComposition`, `Katsumaki`,
        `DemoThere`: silent, no validation errors; not triaged.
      - `NdiOutput`, `PlayVideo`: no NDI / FFmpeg on the Mac yet (expected).
      - Validation noise: unbound `TextureCube` slots get a 2D placeholder view (`mesh-DrawUnlit` CubeMap);
        add a cube placeholder to `VulkanNullResources`.
- [x] **"An update larger than 64 KB needs a staging copy and was dropped."** Fixed 2026-10-08 (all
      platforms): `UpdateResource` on a device-local buffer now copies anything over `vkCmdUpdateBuffer`'s 64 KB
      through a staging buffer of its own (`VulkanCommandList.CopyThroughStaging`).
- [ ] **`PbrTests/PointShading`: last item (Draw Sphere Mesh) sometimes cut off.** Went away after pasting a
      copy of the whole test into a project - looks like timing, initialization or a missing barrier rather than
      shading. Reproduce with the visual suite run twice and compare.
- [ ] **Clean up `RenderToCubemap-vs.hlsl`** (the specular prefilter; over 10 years old). Unused globals outside
      any cbuffer (`objectToWorldMatrix` … `textureMatrix`, `g_CubeSize`, `g_CubeLod`, `g_CubeLodCount` - Slang
      warns about each), a commented-out cbuffer and reference code, the debug `colorOfBox`, inconsistent naming
      and formatting. `UvAndIndexToBoxCoord` and `colorOfBox` are duplicated in `TextureToCubemap-cs.hlsl`; move
      them to a shared include. To show the face in the pixel shader while debugging, add
      `nointerpolation uint face : FACE_INDEX` to `psInput` - Mac only, since the geometry shader on Windows/Linux
      does not pass it on.

### M3 — Distribution
`.app` bundle (Editor + bundled .NET runtime + MoltenVK + slangc + dylibs), `Info.plist`, icon, DMG.
Hardened runtime entitlements: `com.apple.security.cs.allow-jit`,
`allow-unsigned-executable-memory` and `disable-library-validation` (operator packages compiled at runtime
and native libs we don't sign). Developer ID signing and notarization in a GitHub Actions `macos-14`
(arm64) job. Needs an Apple Developer account ($99/yr). Estimate: 10–30 agent hours.

### M4 — Platform extras (later)
Syphon output (the macOS counterpart of Spout), camera input via SDL3, FFmpeg encode.

## Developer setup on a Mac

Machine: a MacBook Air (Apple Silicon, M1 or newer). macOS 14 or newer. Things to know about the Air:

- **RAM is the real limit.** Rider + Editor + `dotnet build` of operator packages + MoltenVK want ~10–14 GB.
  16 GB is comfortable; on 8 GB, close Rider while running the visual suite, and expect swap during builds.
- **Unified memory.** The GPU shares system RAM; MoltenVK reports one device-local heap that is also
  host-visible. The memory-budget warning then measures RAM pressure, not VRAM — check the numbers make sense.
- **Fanless.** Sustained GPU load (visual suite, long sessions) throttles after a few minutes. Fine for
  correctness work; don't use it for performance baselines or frame-time comparisons with Linux/Windows.
- **Small GPU (7–10 cores).** Heavy example projects will run slower than on the Radeon 8060S; that is
  expected, not a port bug.
- **Built-in display is Retina (2x)**, so HiDPI issues show up immediately — useful. An external non-Retina
  monitor tests the 1x path and moving windows between scale factors.
- Run plugged in with Low Power Mode off while testing.

1. **Xcode command line tools:** `xcode-select --install` (git, clang; needed by some NuGet natives).
2. **Homebrew** (https://brew.sh), then `brew install git git-lfs` if the repo uses LFS.
3. **.NET 10 SDK, arm64:** the official installer from dotnet.microsoft.com (not Homebrew's cask — Rider and
   the operator hot reload expect `/usr/local/share/dotnet`). Check: `dotnet --info` shows `osx-arm64`.
4. **Vulkan:** the LunarG Vulkan SDK for macOS, "System Global Installation" checked. It contains MoltenVK,
   the loader, validation layers and `vulkaninfo`, and installs `libvulkan.1.dylib` into `/usr/local/lib`,
   where the loader search finds it. Then run `vulkaninfo --summary` and save the output — it answers the
   API-version and feature questions above in one go. (Homebrew alternative: `brew install molten-vk
   vulkan-loader vulkan-validationlayers vulkan-tools`.)
5. **slangc:** the pinned version (same as Linux, currently 2026.18) from Slang's GitHub releases,
   `macos-aarch64` archive, unpacked to `~/.local/opt/slang-2026.18/`, then
   `xattr -dr com.apple.quarantine ~/.local/opt/slang-2026.18`. Or point `TIXL_SLANGC` at it. Don't use the
   slangc inside the Vulkan SDK — its version differs from the pinned one.
6. **BASS:** download the macOS builds of bass, bassmix and bassflac from un4seen.com into
   `Dependencies/osx/` (to be added to `fetch-bass.sh`).
6b. **FFmpeg (development only):** `brew install ffmpeg@7` - the bindings need FFmpeg 7.x (avcodec-61), and
   Homebrew's plain `ffmpeg` is already 8.x. `FfmpegLibrary` finds the keg-only libraries itself (or the folder
   in `TIXL_FFMPEG_DIR`). Homebrew builds are GPL, so run the Editor with `TIXL_FFMPEG_ALLOW_RESTRICTED=1`;
   shipping needs our own LGPL build (M3).
7. **Rider** for macOS (same license); open `t3.sln`.
8. **Clone:** `git clone` then `git switch feat/linux-port` (or whatever the branch is called then). APFS is
   case-insensitive by default; the repo already has no case-only path duplicates, but a case-sensitive APFS
   volume for the checkout catches the casing bugs Linux would, if you want parity.
9. **Build:** `dotnet build t3.sln -p:EnableWindowsTargeting=true`, then run `Core.Tests`, then the Editor
   with `VK_LOADER_DEBUG=error,warn` and validation layers on.
10. **Debug bridge** works over TCP unchanged — an agent on the Linux machine can drive the Mac's editor over
    the LAN, which keeps the fast loop (edit on Linux, push, pull + run on the Mac).

## Branching

`feat/linux-port` is already more than a Linux port: Vulkan runs on Windows behind a switch, `main` is fully
merged into it, and `main` has no commits it lacks. Recommendation:

1. **Do the Mac work on the same branch.** A separate `feat/macos` branch would recreate the cherry-pick and
   conflict pain this project already had; the Mac changes touch the same files (backend, csproj, startup
   checks).
2. **Renaming is cosmetic; do it at a quiet moment, not as a merge.** If you rename to `feat/cross-platform`:
   push the new name, keep `feat/linux-port` on the remote as a stale alias for a few weeks, and update the
   five references: `.github/workflows/linux-build.yml:7`, `Installer/Linux/aur/PKGBUILD:18`,
   `Installer/Linux/README.md:65`, `Plan_LinuxEditor.md:6`, `Plan_CrossPlatformV5.md:74`. Tell the AUR/Nix
   packagers first.
3. **The real milestone is merging into `main` (4.4)**, after 4.3 ships from `main`. Once there, macOS work
   continues on `main` in small commits, and the branch name stops mattering.

## Open questions

1. ~~MacBook Air — which chip, RAM, macOS version?~~ **Answered:** M4, 24 GB, macOS 26.5.1 (Tahoe). RAM is
   not a constraint. `vulkaninfo` (Homebrew MoltenVK 1.4.2, loader 1.4.363, 2026-10-07): API 1.4.357;
   dynamicRendering, synchronization2, scalarBlockLayout, shaderDrawParameters, push_descriptor,
   shaderOutputLayer, tessellationShader all supported; `VK_KHR_portability_subset` and
   `VK_KHR_portability_enumeration` present; **geometryShader = false**; D24S8 not a depth format (D32S8 is).
   M0 done the same day: `t3.sln` builds, Core.Tests 198/198 pass.
2. Settings folder: `~/.config/TiXL` (like Linux) or `~/Library/Application Support/TiXL`?
3. Cmd/Ctrl mapping policy, and whether Ctrl+click is a right-click.
4. Apple Developer account for signing/notarization — who owns it?
5. Ship the .NET SDK dependency the same way as on Linux (operator compilation needs the SDK, not just the
   runtime), or bundle an SDK inside the `.app`?
6. Is MoltenVK the only ICD, or also test KosmicKrisp (LunarG's Mesa-based Vulkan-on-Metal driver)?
7. Branch rename now, or skip it and go straight for the 4.4 merge?
