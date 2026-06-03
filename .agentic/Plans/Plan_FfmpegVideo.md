# FFmpeg Video Playback (replacing Media Foundation)

## Goal

Replace SharpDX Media Foundation video **decode/playback** with FFmpeg (libav*), giving TiXL broad codec
support, deterministic frame-precise seeking, a GPU-to-GPU hardware-decode path, and HDR awareness, while
keeping the project MIT and shipping a single LGPL FFmpeg shared build.

This is a large multi-session effort. The long-term notes describe the full vision (two cache pipelines,
shared global cache, HAP, TiXLClip, an LGPL/GPL media-worker process, an install assistant). **This plan
covers Milestone 1 only.**

## Milestone 1 scope (locked-in decisions)

- **Replace decode/playback** in [`PlayVideo`](../../Operators/Lib/io/video/PlayVideo.cs) +
  [`PlayVideoClip`](../../Operators/Lib/io/video/PlayVideoClip.cs) with FFmpeg, including a **D3D11VA
  GPU-to-GPU zero-copy path** *and* a **software-decode fallback**.
- **Wrapper:** `Sdcb.FFmpeg` (LGPL) managed bindings. **Ship exactly one FFmpeg shared build**, and
  **remove the duplicate `opencv_videoio_ffmpeg4110_64.dll`** by porting
  [`VideoStreamInput`](../../Operators/Lib/io/video/VideoStreamInput.cs) off OpenCV's `VideoCapture`.
- **Encoding stays on Media Foundation** this milestone (deferred). Keep `SharpDX.MediaFoundation` and the
  [`RenderExport/MF/*`](../../Editor/Gui/Windows/RenderExport/MF) path untouched.
- **Video audio is silent** this milestone. Routing a video's audio track through the BASS `AudioEngine`
  is **backlog** (lower priority). This matches today's behavior — render-export already drops video audio,
  and `PlayVideo`'s audio bypasses BASS entirely.

Intended M1 outcome: scrubbing/looping/playback of common codecs is deterministic and frame-accurate; the
same `double` time always yields the same frame; paused→play has no frame offset; decode never blocks the
main thread; export waits for the exact frame via `Playback.OpNotReady`; one FFmpeg DLL set ships in both
the Editor and the exported Player.

## Current state — what exists

- Decode is MF `MediaEngine` + `TransferVideoFrame` into a B8G8R8A8 texture, duplicated across
  [`PlayVideo.cs`](../../Operators/Lib/io/video/PlayVideo.cs) (with a nested `PlaybackController`) and
  [`PlayVideoClip.cs`](../../Operators/Lib/io/video/PlayVideoClip.cs) (inline). The two diverge subtly.
- Encode is MF `SinkWriter` in [`MfVideoWriter.cs`](../../Editor/Gui/Windows/RenderExport/MF/MfVideoWriter.cs),
  driven by [`RenderProcess.cs`](../../Editor/Gui/Windows/RenderExport/RenderProcess.cs). **Out of scope** for M1.
- [`VideoStreamInput.cs`](../../Operators/Lib/io/video/VideoStreamInput.cs) already decodes RTSP via
  OpenCvSharp `new VideoCapture(url, VideoCaptureAPIs.FFMPEG)` → CPU `Mat` → `UpdateSubresource`. This is
  the only consumer of `opencv_videoio_ffmpeg4110_64.dll` and the migration target.

What is **missing**: the entire FFmpeg decode layer (session, hwaccel backend, software backend, YUV→RGBA
compute converter, deterministic time→frame mapper, per-operator playback controller) and the licensing
guardrail/UI. (The FFmpeg natives are delivered by the runtime NuGet — see Packaging.)

## Key codebase facts (verified)

- Target framework is **`net10.0-windows`** (`Tixl.props`, `Lib.csproj`, `Core.csproj`). Pin the
  Sdcb.FFmpeg wrapper and native build to the **same FFmpeg major** to avoid ABI-mismatch crashes.
- Global device: `T3.Core.Resource.ResourceManager.Device` (SharpDX `Device`), `.ImmediateContext`
  ([ResourceManager.Graphics.cs:24](../../Core/Resource/ResourceManager.Graphics.cs)). Texture wrapper
  `T3.Core.DataTypes.Texture2D` via `Texture2D.CreateTexture2D(desc)`
  ([Texture.cs:56](../../Core/DataTypes/Texture.cs)); implicit-converts to the SharpDX texture;
  `CreateShaderResourceView`/`CreateUnorderedAccessView` helpers exist.
- Compute-dispatch-from-C# reference:
  [TextureBgraReadAccess.cs:235-265](../../Core/Resource/Utils/TextureBgraReadAccess.cs) — get
  `deviceContext.ComputeShader`, save prev shader/UAV/SRV, set shader+SRV+UAV, `Dispatch(gx,gy,1)` (16×16
  groups), restore. Shaders live in `Operators/Lib/Assets/shaders/img/*.hlsl`, compiled at runtime via
  `ResourceManager.CreateShaderResource<ComputeShader>(relPath)` (cs_5_0). **No NV12/P010 shader exists.**
  `SrvManager.GetSrvForTexture` builds only a default, non-arrayed SRV; the D3D11VA texture-array slice
  needs an explicit `ShaderResourceViewDescription`.
- `Playback.OpNotReady` is a `static bool` ([Playback.cs:90](../../Core/Animation/Playback.cs)); the export
  loop short-circuits on it
  ([RenderProcess.cs:450](../../Editor/Gui/Windows/RenderExport/RenderProcess.cs)). The `OpNotReady` line in
  [PlayVideo.cs:70](../../Operators/Lib/io/video/PlayVideo.cs) is commented out and must be re-enabled
  (export-gated, matching old MF behavior, so realtime returns last-valid instead of stalling).
- Native DLLs ship as **flat files** copied from repo-root `/Dependencies/**` into output (`Editor.csproj`,
  `Player.csproj` globs) and resolved by `TixlAssemblyLoadContext.NativeDllResolver`
  ([TixlAssemblyLoadContext.cs:424-520](../../Core/Compilation/TixlAssemblyLoadContext.cs)) via
  `NativeLibrary.TryLoad`. Optional/user-supplied native precedent:
  [SwiftCamDevice.cs:87-103](../../Operators/Lib/io/video/SwiftCamDevice.cs).
- Player export maps operator GUIDs → required native DLLs in
  [PlayerExporter.cs:527-581](../../Editor/UiModel/Exporting/PlayerExporter.cs);
  `GetUnusedMappedDependencyFiles` auto-excludes DLLs for unused ops. `VideoStreamInput`'s GUID currently
  pulls in `opencv_videoio_ffmpeg4110_64.dll` (line 553); `PlayVideo`/`PlayVideoClip` are in no definition.

## Class structure (dedicated `Video.csproj`, namespace `T3.Video`)

The FFmpeg decode infra lives in a **dedicated sibling `Video.csproj`** (repo root, alongside `Core`,
`Logging`) — *not* in `Core` (keep Core minimal) and no longer loose in `Lib`. Extracted early (done) so a
`Video.Tests` xunit project can unit-test decode/seek/cache against the real sample videos without dragging
in all of `Lib`. `Video.csproj` references `Core` + `Logging` + the managed `Sdcb.FFmpeg` bindings; `Lib`
references `Video.csproj` (copied into the operator package) and carries the **native** runtime package so
the DLLs ship with the package. Verified by build: `Video.dll` + `runtimes/win-x64/native/*` land in
`Operators/lib/`, while `Core.dll` stays host-provided. The public API (`FfmpegLibrary`,
`TimeToFrameMapper`, `VideoDecoderSession`) is `public`; tests see internals via `InternalsVisibleTo`.

- **`FfmpegLibrary`** (static) — idempotent, thread-safe init: trigger native load via `av_version_info()`,
  read `avcodec_configuration()`, **reject `--enable-gpl`/`--enable-nonfree`** builds, log version. Sdcb uses
  direct `[DllImport]`; no `RootPath` is needed — natives resolve via `Lib.deps.json`. **Done.**
- **`TimeToFrameMapper`** — pure, unit-testable. `double seconds → target PTS` by **floor-to-PTS** (the
  frame whose `[pts, pts+duration)` interval contains the time); loop wrap (`MathUtils.Fmod` on duration)
  vs clamp-to-`[first,last]`. The determinism core. **Done** (unit tests pending a test-project home).
- **`VideoDecoderSession`** — owns one `AVFormatContext` + video `AVCodecContext`; duration/timebase/size/
  pixfmt + **HDR detection** (color_trc/primaries, P010/P016 → 10-bit). Two decode modes: **sequential
  read-ahead** (decode-next into a small queue — the *default*, fast path) and **exact seek**
  (`av_seek_frame BACKWARD` → `avcodec_flush_buffers` → decode-forward to target), used only on a
  discontinuity. Builds a lazy **keyframe/PTS index** (GOP boundaries) for seek targeting and M2 caching.
  Single-owner (one worker thread).
- **`IDecodeBackend`** + **`D3D11VaBackend`** (primary, zero-copy) and **`SoftwareBackend`** (CPU fallback).
- **Conversion is split by decode path** (the software path decodes to *planar YUV420P*, not NV12):
  - **`SoftwareFrameConverter`** (software path) — CPU swscale YUV→packed **RGBA8** (SDR) / **Rgba64le**
    (HDR); the caller uploads the result to a texture. Low-risk, unit-testable. **Done + tested.** (Notes
    explicitly allow swscale as the software fallback.)
  - **GPU NV12/P010→RGBA compute converter** (hardware path, folded into the D3D11VA step) — binds two plane
    SRVs (luma R8/R16, chroma R8G8/R16G16) off the decoder's GPU texture and dispatches a new HLSL compute
    shader into the output UAV (no CPU round-trip). RGBA8 (SDR) / RGBA16_Float (HDR); tone-map stub in M1.
    Follows the `TextureBgraReadAccess` save/restore dispatch pattern.
- **`VideoPlaybackController`** — per-operator state machine + decode worker thread; owns session,
  last-valid `Texture2D`, not-ready flag, seek/play/loop. Public surface mirrors what the operators need
  (`HandleGettingFrames(...)`, `Texture`, `Duration`, `HasCompleted`, `ErrorMessageForStatus`,
  `IsReadyForRendering`). Replaces the nested controller in `PlayVideo` and the inline MF machinery in
  `PlayVideoClip`, so both operators share identical determinism.

## D3D11VA device sharing (true zero-copy; riskiest piece)

*D3D11VA is the **vendor-neutral** Direct3D 11 decode API — AMD, Intel, and NVIDIA all implement it; it is
**not** NVIDIA-locked (NVDEC/CUDA is deliberately avoided). GPUs lacking a given codec profile fall back to
the software path, which — together with the M2 RAM cache — is fully vendor-neutral.*

1. Once globally: `ResourceManager.Device.QueryInterface<DeviceMultithread>().SetMultithreadProtected(true)`
   (MF code already does this — required: the decoder's `ID3D11VideoContext` runs on the worker while the
   render thread uses the immediate context).
2. `av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_D3D11VA)` → set `AVD3D11VADeviceContext.device =
   ResourceManager.Device.NativePointer` (**AddRef** the COM device — FFmpeg Releases on teardown; a
   refcount slip double-frees the global device → app crash). Wire FFmpeg's `lock`/`unlock` callbacks to a
   shared mutex that `NvToRgbaConverter.Dispatch` also takes. `av_hwdevice_ctx_init`, then
   `codecContext.hw_device_ctx = av_buffer_ref(...)` before `avcodec_open2`; `get_format` returns
   `AV_PIX_FMT_D3D11`.
3. Decoded hw `AVFrame`: `data[0]` = `ID3D11Texture2D` **array**, `data[1]` = **array slice index**. Wrap
   `data[0]` as a SharpDX texture (no copy), build explicit luma+chroma `ShaderResourceView`s with
   `Texture2DArray`/`FirstArraySlice = (int)data[1]`, convert into the operator's output texture. Cache SRVs
   by slice index (FFmpeg reuses slices); invalidate on size/format change.
4. Convert always runs on the **render thread** inside `Update()`; the worker never touches the immediate
   context. Hold the `AVFrame` ref until convert is submitted, then `av_frame_unref` to free the slice.

**Fallback if shared-device is unstable:** FFmpeg creates its own D3D11 device, decode there, copy the slice
into a `Shared`/`KeyedMutex` texture, open it on `ResourceManager.Device`, convert (one GPU-GPU copy, still
no CPU round-trip). Reuses the same converter + controller. The **software path is the guaranteed shippable
baseline** — if D3D11VA can't be stabilized in M1, zero-copy slips to M1.x.

## Threading & determinism

- One worker thread per controller (mirrors
  [VideoStreamInput.cs:120](../../Operators/Lib/io/video/VideoStreamInput.cs) + `CancellationToken`/`Join`).
  The worker's **default mode is sequential read-ahead**: decode-next into a small forward queue and keep it
  full. It **only seeks** when the requested PTS jumps outside the queue (scrub / random jump): flush queue →
  `av_seek_frame BACKWARD` → decode-forward to target. The main thread posts a **latest-wins** target PTS
  (`volatile long` + `AutoResetEvent`) and pulls from the queue. One design serves every pattern: forward
  play and **export** = queue pops (no seeks, fast); scrub = flush+seek+refill.
- **Export is the payoff.** Because export advances time monotonically, it stays in sequential read-ahead and
  **never per-frame-seeks** — fixing the MF failure where each export frame triggered a long-GOP seek +
  `OpNotReady` stall (~4× slowdown). Read-ahead keeps the queue full so the exporter (which can outrun
  wall-clock) almost never waits; `OpNotReady` remains only as a rare safety. Rate mismatch is handled by
  *deliver-not-seek*: per export timestamp, pop the floor-to-PTS frame, dropping/duplicating as needed —
  source PTS still advances monotonically, never a backward seek.
- Race-free handoff: a `lock`-guarded single-slot `PendingFrame`; `Update()` runs the convert on the
  immediate context, sets `Texture`, releases the frame. Never blocks — if the exact frame isn't ready,
  return last-valid and set `Playback.OpNotReady` (export-gated).
- **No start-offset priming:** delete the MF `playbackOffset = precisePlayback ? 2/60f : 0` hack
  ([PlayVideo.cs:172](../../Operators/Lib/io/video/PlayVideo.cs)). FFmpeg gives direct PTS control, so there
  is no start latency to compensate — this fixes the paused→play frame offset. Keep the `IsPreciseAtPlayback`
  input slot/GUID for graph compatibility but make it a semantic no-op.
- `PlayVideoClip` keeps its timeline→source-time mapping (`TimeRange`/`SourceRange`, per-clip rate at
  [PlayVideoClip.cs:87-96](../../Operators/Lib/io/video/PlayVideoClip.cs)); the computed `shouldBeTimeInSecs`
  feeds the same controller (clamp branch).

## Packaging & single-DLL consolidation

- **FFmpeg DLLs are delivered by NuGet, not committed to git** (avoids ~137 MB of binaries / the GitHub
  100 MB-per-file limit; no Git LFS). Managed `Sdcb.FFmpeg` bindings live in `Video.csproj`; the **native**
  runtime package is referenced by [`Lib.csproj`](../../Operators/Lib/Lib.csproj). The package drops the
  DLLs into `runtimes/win-x64/native/` — but TiXL's operator loader
  (`TixlAssemblyLoadContext.LoadUnmanagedDll`) resolves natives **flat** next to the operator assembly, not
  from `runtimes/native` (deps.json native resolution only works for the default ALC / test host, *not* the
  editor's custom operator ALC). So a `FlattenFFmpegNatives` MSBuild target in `Lib.csproj` copies the DLLs
  up next to `Lib.dll`; they then ride the operator-package copy (and shadow-copy) into the editor/player.
  Verified: the flat DLLs appear next to `Lib.dll`.
- **Editor load-context integration (three hard-won fixes, all needed):** getting Sdcb.FFmpeg to load inside
  TiXL's operator `AssemblyLoadContext` required: **(1)** the flat-DLL target above; **(2)** `FfmpegLibrary`
  **pre-loads** the FFmpeg DLLs by full path, `avutil` first — because once `avcodec` is found, the OS
  resolves *its* dependencies (`avutil`/`swresample`) by name through the standard search order, which
  excludes the operator dir; **(3)** a small skip in
  [`AssemblyTreeNode`](../../Core/Compilation/AssemblyTreeNode.cs): a hardcoded
  `_assembliesWithOwnNativeResolver` set (currently `"Sdcb.FFmpeg"`) that suppresses TiXL's
  `DllImportResolver` registration for those assemblies. Sdcb registers its *own* resolver in its static
  ctor, and .NET allows only one per assembly, so pre-empting it threw `InvalidOperationException: A resolver
  is already set` and poisoned the `ffmpeg` type. **Note:** an attempt to make this an assembly attribute
  (`[SelfManagedNativeResolver]`) declared by `Video` was reverted — reading custom attributes
  (`GetCustomAttributes`) inside the `AssemblyTreeNode` ctor runs *on the assembly-load path* and triggers
  **reentrant assembly resolution**, which broke loading of *every* operator package. Matching by assembly
  name (`GetName().Name`) is metadata-only and safe; the name must stay in Core. With all three,
  `PlayVideo` decodes and displays in the editor (verified). *(These are integration tax specific to the
  operator-ALC; the unit-test host needed none of them.)*
- **⚠ Licensing — open item (the headline risk).** `Sdcb.FFmpeg.runtime.windows-x64` is a **GPL build**
  (`--enable-gpl --enable-version3`, x264/x265) — confirmed via `avcodec_configuration()`, and **rejected by
  the license guardrail**. Fine for local dev/test (decode is identical; GPL is a *distribution* concern via
  the test opt-in) but it **must not ship**. Before release, swap the native runtime to a verified **LGPL
  FFmpeg 7.0 (avcodec-61)** source. Options: (a) the **`ffmpeg.lgpl`** NuGet pinned to its avcodec-61
  (FFmpeg-7.0) date-version — keeps all code, NuGet-delivered, but a third-party package; or (b) a
  **self-hosted/vendored BtbN `lgpl-shared` 7.0** build (the notes' "custom runtime NuGet with verified LGPL
  DLLs") — full control + reproducibility, more setup. **Decision pending.** Ship the FFmpeg LGPL license
  text + attribution in `Dependencies/licenses/` regardless. New HLSL shader(s) go under
  `Operators/Lib/Assets/shaders/img/`.
- [`PlayerExporter.cs`](../../Editor/UiModel/Exporting/PlayerExporter.cs): add a new `OpDependencyDefinition`
  mapping the three video GUIDs (`914fb032…` PlayVideo, `04c1a6dc…` PlayVideoClip, `D9A7233D…`
  VideoStreamInput) → the FFmpeg DLL set. After porting `VideoStreamInput`, **remove
  `opencv_videoio_ffmpeg4110_64.dll`** from the OpenCV bundle (line 553). OpenCV core stays.

## Licensing guardrail + UI

- `FfmpegLibrary` refuses GPL/nonfree builds at init — **done, and it already caught the GPL Sdcb runtime**
  (see Packaging). Two off-by-default, loudly-warned dev escape hatches let local runs use whatever build is
  installed: `AllowRestrictedBuildForTesting` (set by the test module-init) and the
  **`TIXL_FFMPEG_ALLOW_RESTRICTED=1`** env var (for running the editor against the GPL build until the LGPL
  one is sourced). The shipped editor (no env var) always enforces LGPL. Operators surface `StatusError` via
  `IStatusProvider`.
- TODO: add an "FFmpeg `<version>` — LGPL shared build" line to
  [`AboutDialog.cs`](../../Editor/Gui/Dialog/AboutDialog.cs) info block + copyable system info.

## Phasing (build-verifiable; `dotnet build` after each step)

1. Commit this plan (done).
2. Scaffold `_ffmpeg/` + `Sdcb.FFmpeg` (managed) + `Sdcb.FFmpeg.runtime.windows-x64` (natives via NuGet)
   packages + `FfmpegLibrary` init; confirm native load + log version. *(Verifies packaging before any
   decode logic.)* **Done** — DLLs confirmed delivered to `runtimes/win-x64/native/`, Lib builds green.
3. **Done.** Extracted `_ffmpeg/` → `Video.csproj` (+ `Video.Tests`); `TimeToFrameMapper` + tests (pure;
   determinism locked). Solution + native delivery verified.
4. **Done.** `VideoDecoderSession` (open/seek/decode, duration/format/HDR, sequential + exact-seek). Verified
   by `Video.Tests` against `test-720p.mp4` / `spray-1080p.mp4`: metadata, monotonic PTS, frame-accurate +
   deterministic seeks (identical landed PTS *and* luma checksum). 22/22 tests green.
5. **Software converter done.** `SoftwareFrameConverter` (swscale YUV→RGBA8/RGBA16) + tests — full
   decode→RGBA verified deterministic (24/24 green). Remaining for first light: the texture upload (lands
   with the operator rewire) and the GPU compute converter (moved into the D3D11VA step).
6. **Done & verified in the editor.** `PlayVideo` rewired to `VideoPlaybackController` (synchronous for now —
   decode/convert/upload in `Update()`, sequential vs seek-on-discontinuity); RGBA→`Texture2D` upload; MF
   machinery removed; export-gated `Playback.OpNotReady` re-enabled. **First visible frame achieved** —
   video displays and scrubs in the editor, performance comparable to MF (main-thread blocking as expected).
   **Follow-up done:** decode + convert moved to a per-controller **worker thread**; the render thread only
   uploads the latest ready RGBA buffer (D3D immediate context stays on the render thread). Latest-wins
   target; sequential-vs-seek on the worker; worker exceptions are contained (a background-thread throw would
   otherwise kill the editor). Realtime playback is async (last-valid texture meanwhile); **export blocks
   per-frame until the requested frame is decoded** (`renderingToFile` → `WaitForRequestedFrame`, bounded by
   a timeout) — without this, the worker's extra frame of latency defeated the exporter's one-frame-lag
   compensation and prepended a stale frame.
7. **Code done (pending editor verify).** `PlayVideoClip` rewired onto the same `VideoPlaybackController`,
   preserving the TimeClip→source-time mapping (`TimeRange`/`SourceRange` + per-clip rate, clamped to the
   clip's source range); MF machinery removed; now also an `IStatusProvider`. Inherits the worker thread +
   export-sync automatically. Verify timeline scrub + render-to-file.
8. **`D3D11VaBackend` zero-copy** (riskiest) behind a runtime flag, auto-fallback to software on init
   failure. Verify H.264 8-bit + HEVC 10-bit (HDR → RGBA16, stub tone-map).
9. Port `VideoStreamInput` to `VideoDecoderSession`; update `PlayerExporter` (add FFmpeg definition, remove
   opencv ffmpeg plugin). Verify export includes/excludes FFmpeg DLLs by op usage.
10. Licensing guardrail surfacing + AboutDialog line. Cleanup dead MF **decode** code (keep MF **encode**).

**Riskiest = step 8.** Fallbacks, in order: own-device + shared-texture (keyed mutex) → software path
(steps 4-7 are a complete, shippable M1 without zero-copy).

## Verification

**Decode core — already verified standalone (✅).** A throwaway probe ran the exact `VideoDecoderSession`
FFmpeg sequence against `Operators/examples/Assets/videos/test-720p.mp4`: metadata correct (1280×720,
Yuv420p, timebase 1/60000, 60 s, 60 fps); sequential PTS strictly monotonic; forward seek frame-accurate
(2.0 s → PTS 120000); backward seek correct; and **determinism confirmed** — the same time decoded twice
gave identical landed PTS *and* identical luma checksum, even after intervening seeks. The seek strategy
works.

**Testing strategy (two tracks):**
- **End-to-end (integration):** use the existing `VisualTest`/`ExecuteTests` operator harness
  ([Operators/examples/testing/VisualTest.cs](../../Operators/examples/testing/VisualTest.cs)) — it already
  steps a `TimeRange`, honors `Playback.OpNotReady`, and was *explicitly built to wait on video seeking*
  (see its `UpdateTestParams` note). Add a test composition wiring `PlayVideo`(test-720p.mp4 / spray-1080p.mp4)
  → `VisualTest` with low-res reference PNGs, once `PlayVideo` is rewired (step 6). This catches
  decode/seek/determinism regressions visually in the editor's Guided-Tests runner.
- **Decode/seek/cache unit tests (CI-able):** the decode + seek + (M2) cache logic is D3D-free and
  unit-testable against the real videos as the probe showed. **This is the strongest reason to extract the
  `_ffmpeg/` infra into a dedicated `Video.csproj`** (the "promote-later" note): a `Video.Tests` project can
  then reference it + the Sdcb packages and assert PTS-determinism / frame-accuracy / cache-hit behavior
  without pulling all of `Lib` or needing the editor. (Decision pending — see report.)
- **Unit (pure):** `TimeToFrameMapper` tests — same `double` ⇒ same PTS; loop wrap/clamp; back-step.
- **Editor, manual:** drop a video on `PlayVideo`; scrub — frames deterministic and frame-accurate; pause
  then play — no frame jump; toggle Loop — wrap vs clamp-to-first/last; play a 10-bit HEVC — output switches
  to RGBA16 (washed colors acceptable, tone-map stub); a late frame returns last-valid, never freezes the UI.
- **TimeClip:** `PlayVideoClip` — trim/scale a clip, scrub, confirm source-time mapping holds.
- **Export:** render-to-file an `Output` driven by `PlayVideo`/`PlayVideoClip` — `Playback.OpNotReady` makes
  the exporter wait for the exact frame (no skips), and forward export **streams sequentially with read-ahead,
  never per-frame-seeking** (compare wall-clock against the old MF path — should be markedly faster). (Audio
  absent by design.)
- **D3D11VA:** confirm hardware path engages (log slice indices); forcing the software flag yields identical
  frames; graceful fallback when hwaccel init fails.
- **Packaging:** fresh Editor run loads FFmpeg from output dir (version logged, license check passes);
  export a project using a video op → FFmpeg DLLs present; one that doesn't → excluded;
  `opencv_videoio_ffmpeg4110_64.dll` no longer shipped once `VideoStreamInput` is ported.
- Add a manual test set under `.tests-manual/` for video playback determinism.

## Milestone 2 — Caching, prefetch & reverse (deferred; design locked)

Design center is **large-GOP H.264/HEVC, 1080p @ 30/60** (what most users drop in), *not* HAP. Decode is
sequential within a GOP, so the cache must respect GOP structure. M1's sequential read-ahead already covers
forward play/export; M2 adds retention for random seek, framewise stepping, and reverse.

- **Cache tier = CPU/RAM, not GPU.** Upload-on-display is cheap (1080p NV12 ≈ 3.1 MB ≈ 0.25 ms over PCIe; MF
  already does CPU upload acceptably) and RAM is plentiful/uncontended (a 4 GB budget holds ~1300 1080p-NV12
  frames ≈ 22 s @60). Zero-copy GPU decode stays the *forward-playback* path; a small GPU "hot window" is an
  *optional* later optimization, not core. Fully vendor-neutral.
- **No GC pressure (firm constraint).** Frame bytes never live on the managed heap. Software-decoded frames
  are retained as **native, refcounted `AVFrame`s** (`av_frame_ref`/`unref`; FFmpeg pools them) — zero-copy
  *into* the cache, no managed allocation. Hardware frames (D3D11VA's fixed ~20-slot ring can't be retained)
  are **copied out** into pool-owned native blocks (`NativeMemory.Alloc`). GPU hot-window textures use
  explicit `Dispose` (never finalizer). The managed side is small bookkeeping (LRU dict, entry handles), kept
  allocation-free per TiXL rules. A churning multi-GB managed/LOH cache would cause gen2 pauses → the exact
  scrubbing stutter the cache exists to remove.
- **Format heterogeneity = a byte budget, not a typed pool.** Entry =
  `{videoGuid, pts, pixfmt, w, h, nativeBuffer}`. Each video produces uniform frames → **per-video
  homogeneous sub-allocations** (trivial slab reuse); the global manager arbitrates **bytes** across videos,
  never a common format. Store the **source format (NV12/P010)** and convert on display (~2.5× more
  frames/byte than RGBA; defers HDR tone-map). Mixed formats across videos = different buffer sizes = still
  just bytes.
- **GOP-band cache (avoids the "lonely frame" trap).** Cache **contiguous runs / whole GOPs**, not isolated
  frames — a lone frame is useless because the next needs a keyframe seek. **LRU evicts whole runs**, scored
  by distance-from-playhead × direction, so the surviving cache stays usable contiguous bands (prevent
  fragmentation rather than defragment after the fact). The lazy keyframe index drives GOP-aligned fills.
- **Direction-aware prefetch.** Per player, maintain a directional window (ahead-biased forward,
  behind-biased reverse, both for stepping), sized/weighted by the prefetch-priority table (1-fwd=9,
  1-back=3, ±1s=3, ±10s=3, ±100s=1…). Shared global budget across all players, ref-counted (a displayed
  frame is pinned; unloading a video / disposing a player / replacing a file drops its entries).
- **Reverse playback** = decode the covering GOP forward into the cache, **emit backward** from it (back-step
  becomes a cache hit, GOP size irrelevant), with **read-behind** pre-warming the previous GOP at the
  boundary. Same mechanism as the GOP-band cache, direction flipped.
- **M1 seams that keep this cheap to add (build these in M1):** frames identified by PTS (✅ mapper); the
  controller pulls frames through a thin `IFrameSource.TryGet(pts)` (decode-only in M1, cache-backed in M2);
  the display frame is a *descriptor* that may be a GPU texture (zero-copy) **or** a native RAM buffer
  (cache) — the converter accepts either input; sequential read-ahead already lands in M1.

## Backlog / deferred (beyond M1 & M2)

- **Encode milestone** (replace MF `SinkWriter` with FFmpeg, LGPL/GPL split — external `ffmpeg.exe` worker
  for GPL codecs). **Includes the background "optimizer": auto-transcode imported media to a seek-optimized
  sibling (small-GOP / all-intra `name-optimized.mp4`), auto-preferred when present, so large-GOP random seek
  gets cheap without the user thinking about codecs** (the notes' `OptimizerService`). This is the real lever
  that makes random seeking fast for everyday media — a "killer feature," explicitly wanted in the encode plan.
- Route a video's audio track through the BASS `AudioEngine` (new push-stream `BASS_STREAMPROC`; no existing
  pattern). Lower priority.
- Full HDR tone-mapping (PQ/HLG → linear); HAP fast-path (reuse the [DDS BCn upload](../../Core/Resource/Dds/DdsDirectX.cs));
  TiXLClip cache/bake; the media setup/install assistant.
