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
- **Encoding stays on Media Foundation** this milestone (deferred to the **encode milestone** —
  [`Plan_FfmpegEncode.md`](Plan_FfmpegEncode.md)). Keep `SharpDX.MediaFoundation` and the
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
- **Licensing — RESOLVED (the headline risk).** `Sdcb.FFmpeg.runtime.windows-x64` was a **GPL build**
  (`--enable-gpl --enable-version3`, x264/x265) — confirmed via `avcodec_configuration()` and rejected by the
  license guardrail. **Swapped 2026-06-03** for the **`FFmpeg.LGPL`** NuGet package (`20250329.1.0`), a BtbN
  `lgpl-shared` build: avcodec-61 (FFmpeg 7.x, matches the `Sdcb.FFmpeg` 7.0.0 bindings, which stay
  unchanged), configured **without** the gpl/nonfree flags and without x264/x265 — statically verified by
  scanning the shipped `avcodec-61.dll`'s embedded configure string (`--enable-gpl`/`--enable-nonfree`/
  `--enable-libx264` all absent). Its `.targets` copies the av/sw DLLs **flat** into the operator output
  (verified by a build), which is where `FfmpegLibrary` loads them, so the guardrail now passes **without**
  the dev opt-in. Residual notes:
  - This build enables `--enable-version3` → it is **LGPL v3** (not v2.1). We ship the LGPL v3 text; LGPL v3
    incorporates GPL v3 (already present as `GPL-v3-EmguCV.txt`).
  - Pinned to `20250329.1.0`, the **newest avcodec-61 (FFmpeg 7.x)** FFmpeg.LGPL build — `20250330` bumped to
    avcodec-62 (the FFmpeg 8.x-dev SONAME, from the "libs: bump major version" commit), whose ABI the
    `Sdcb.FFmpeg` 7.0.0 bindings do **not** match. Crossover found by range-reading each candidate nupkg's zip
    directory. To go past 7.x, bump `Sdcb.FFmpeg` to a matching major and pin a same-SONAME FFmpeg.LGPL build.
  - It is a broad BtbN "everything" build (~100 MB of DLLs, statically bundling dav1d/libaom/libvpx/…); a
    minimal custom LGPL build is the smaller-footprint alternative if size or third-party attribution matters.
  - **Pending: runtime confirmation** (editor restart) that the new DLLs load + decode and the guardrail
    passes with no opt-in.
  Shipped the FFmpeg LGPL v3 license text + attribution as
  [`Dependencies/licenses/LGPL-v3-FFmpeg.txt`](../../Dependencies/licenses/LGPL-v3-FFmpeg.txt) (FFmpeg
  attribution + bundled-library note + the verbatim GNU LGPL v3); the `Dependencies/**` content glob in
  `Editor.csproj`/`Player.csproj` copies it into both the editor and the exported player. New HLSL shader(s)
  go under `Operators/Lib/Assets/shaders/img/`.
- [`PlayerExporter.cs`](../../Editor/UiModel/Exporting/PlayerExporter.cs): add a new `OpDependencyDefinition`
  mapping the three video GUIDs (`914fb032…` PlayVideo, `04c1a6dc…` PlayVideoClip, `D9A7233D…`
  VideoStreamInput) → the FFmpeg DLL set. After porting `VideoStreamInput`, **remove
  `opencv_videoio_ffmpeg4110_64.dll`** from the OpenCV bundle (line 553). OpenCV core stays.

## Licensing guardrail + UI

- `FfmpegLibrary` refuses GPL/nonfree builds at init — **done, and it already caught the GPL Sdcb runtime**
  (see Packaging). Two off-by-default, loudly-warned dev escape hatches let local runs use whatever build is
  installed: `AllowRestrictedBuildForTesting` (set by the test module-init) and the
  **`TIXL_FFMPEG_ALLOW_RESTRICTED=1`** env var (now only needed if a developer deliberately points at a
  GPL/non-free build; the shipped `FFmpeg.LGPL` runtime passes the guardrail on its own). The shipped editor
  (no env var) always enforces LGPL. Operators surface `StatusError` via `IStatusProvider`.
- **Done.** The About dialog now shows an `FFmpeg: <version> (LGPL)` line (or
  `(GPL/non-free — development build)` on a dev machine) in both the
  [`AboutDialog.cs`](../../Editor/Gui/Dialog/AboutDialog.cs) System Information block and the copyable
  system-info text. Cross-context bridge: `FfmpegLibrary` lives in `Video.dll` (operator load context) while
  the dialog lives in the editor, so `FfmpegLibrary.Initialize()` registers its version/license line into a
  new shared-Core registry — [`ThirdPartyRuntimeInfo`](../../Core/Resource/ThirdPartyRuntimeInfo.cs)
  (Core is the assembly shared across both load contexts) — and the dialog reads it back. The line registers
  lazily on first video use, so it only appears once a video op has initialised FFmpeg.

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
   failure. Verify H.264 8-bit + HEVC 10-bit (HDR → RGBA16, stub tone-map). **Detailed implementation plan:**
   [`Plan_VideoZeroCopyDecode.md`](Plan_VideoZeroCopyDecode.md) — the backend seam, the D3D11VA device-sharing
   sequence, the worker→render handoff rework, the GPU converter + shader, the fallback tiers, and the phased,
   GPU-verifiable build order.
9. **Code done (pending live-RTSP verify).** `VideoStreamInput` ported off OpenCV `VideoCapture(FFMPEG)` to
   `VideoDecoderSession` + `SoftwareFrameConverter` (sequential decode of the live stream; RTSP gets
   `rtsp_transport=tcp` + a socket `timeout` via a new `TryOpen(..., demuxerOptions)` overload). The dead
   **opencv FFmpeg plugin is dropped** from Lib's output via a `DropUnusedOpenCvFfmpegPlugin` MSBuild target
   (no op uses OpenCV's video backend now); `PlayerExporter` no longer maps `VideoStreamInput` to the OpenCV
   bundle, so a stream-only project ships no OpenCV. *(Note: the FFmpeg DLLs always ship with the Lib
   operator package — the operator-package copy doesn't apply file-exclusion, unlike the Player-dir copy. A
   per-project FFmpeg exclusion would need the export to exclude operator-package files too — deferred.)*
10. **Done.** Licensing surfacing: About-dialog `FFmpeg: <version> (LGPL)` line via the shared-Core
    `ThirdPartyRuntimeInfo` registry (see *Licensing guardrail + UI*). Dead MF **decode** sweep verified a
    no-op — the `PlayVideo`/`PlayVideoClip`/`VideoStreamInput` rewrites already removed every MF decode usage
    from the operators (`grep` for `MediaEngine`/`MediaFoundation` in `Operators/**` is empty; no leftover MF
    package reference in `Lib.csproj`); the only remaining MF code is the **encoder** in
    `Editor/Gui/Windows/RenderExport/MF/*`, kept only until the **encode milestone** removes it
    ([`Plan_FfmpegEncode.md`](Plan_FfmpegEncode.md)). Manual test set
    [`video-playback-determinism.md`](../../.tests-manual/video-playback-determinism.md) added.

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
- **Done.** Manual test set [`video-playback-determinism.md`](../../.tests-manual/video-playback-determinism.md)
  added under `.tests-manual/` (first frame, About-dialog FFmpeg line, deterministic scrub, paused→play with
  no offset, loop-vs-clamp, fast-scrub last-valid, `PlayVideoClip` source-range mapping, render-to-file
  frame alignment).

## Milestone 2 — Caching, prefetch & reverse (in progress)

**Implemented (per-controller, software path):** the native ref-counted frame cache
([`VideoFrameCache.cs`](../../Video/VideoFrameCache.cs) — PTS-keyed, byte-budgeted LRU, frames retained as
`Frame.Clone()` so pixel bytes stay native) wired into the worker so every frame decoded along a
seek/sequential pass is cached and a revisited frame skips decode; plus **forward read-ahead** (the worker
decodes ~0.5 s past the playhead into the cache, never seeking, bailing on scrub). 512 MB/controller for now.
**Remaining:** GOP-band eviction (current is pure-recency LRU), reverse read-behind, the engine-centralized
shared budget, and the `Optimize for` on/off gating.

Design center is **large-GOP H.264/HEVC, 1080p @ 30/60** (what most users drop in), *not* HAP. Decode is
sequential within a GOP, so the cache must respect GOP structure. M1's sequential read-ahead already covers
forward play/export; M2 adds retention for random seek, framewise stepping, and reverse.

**Two pipelines, selected per use-case — not one tuned pipeline.** The decode→convert core is shared, but
memory policy diverges and the two compete for the same GPU/RAM budget, so a heavy clip picks *one*:
- **(A) Seeking — NLE / procedural VJ:** latency-bound, frames revisited (scrub, loop, step, reverse).
  → **software decode + RAM GOP-cache** (this section). Cache is the lever; upload/decode cost is secondary.
  Vendor-neutral, no GPU device-sharing risk.
- **(B) High-res throughput — parallel 4K+ streams:** bandwidth-bound, each frame plays once, forward.
  → **D3D11VA zero-copy** (M1 step 8): convert-and-release, **no cache** (retaining huge once-used frames is
  pure waste). Throughput is the lever; caching is irrelevant.
These are near-mutually-exclusive for a given heavy clip: caching needs retainable RAM frames (⇒ software
decode), zero-copy needs the decoder's fixed hardware ring (⇒ no retention). Path selection is therefore an
**explicit operator parameter** — `Optimize for: { Fast Seeking, Playback Performance }` on
`PlayVideo`/`PlayVideoClip` (default **Fast Seeking**) — not a heuristic and not a global mode:
- `Fast Seeking` → pipeline **A** (software decode + RAM GOP-cache).
- `Playback Performance` → pipeline **B** (D3D11VA zero-copy, no cache), **falling back to
  software-without-cache** if hwaccel init fails.
The enum expresses *intent*; the controller picks the best available backend and re-inits if the value
changes at runtime. This also simplifies the budget story — only `Fast Seeking` clips draw on the shared
cache budget. Add the input only once both backends exist (don't ship a dead parameter). The `IFrameSource`
+ frame-descriptor seam (below) is what lets both share the converter and the controller.

**Also codec-gated.** The cache (and the whole A/B choice) only matters where decode is expensive —
**long-GOP H.264/HEVC**. All-intra codecs (ProRes/DNxHD/MJPEG, all-intra H.264) seek cheaply (every frame a
keyframe) → decode-on-demand, no GOP-cache. **HAP and GPU-texture codecs (HAP Q/Alpha, NotchLC, DXV) bypass
it entirely → GPU→GPU upload, no RAM cache** — their compressed form *is* the efficient GPU-resident form,
so caching decoded RGBA would discard the point. Effective pipeline = **(codec class) × (`Optimize for`
intent)**; the codec can override the param (HAP is always GPU). The companion
[`Plan_VideoClipPlayer.md`](Plan_VideoClipPlayer.md) covers this codec table **and** the many-clip
**decode pool** (a `VideoClipPlayer` owning N pooled controllers — temporal scheduling, preroll of upcoming
clips, eviction of far ones — so hundreds of timeline clips don't mean hundreds of live decoders).

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
- **Realtime predictive seek (live, beat-locked clock).** *Realtime-only* — export waits for the exact frame
  (`OpNotReady`) and NLE scrub can pause; only the beat-locked live clock can't, because the seek target
  moves *while you decode it*. A cache-miss seek must target **where the clock will be when the decode
  lands**, not where it is now: `t_target = t_now + rate × T_seek_est`. Deliberately **overshoot**
  (`T_seek_est = EWMA(measured) × (1 + margin)`) so you land slightly *ahead* and **hold the frame until the
  clock reaches its PTS** — landing ahead is stable; landing behind chases. Feeding measured seek time back
  makes it a converging control loop (a Smith predictor for the decode transport delay; the beat-lock makes
  the clock's future position exactly predictable — an asset). **Two failure modes, only one is a seek
  problem:** (1) transient seek latency → the predictor fixes it; (2) *sustained* decode-slower-than-realtime
  → no predictor helps (realtime re-passes the target each cycle) → **degrade**: frame-skip → keyframe-only
  → request hardware/proxy. Detect via "can't land ahead after k tries." Only bites **long-GOP + cache-miss
  + live clock**; cache hits, preroll, and HAP/intra avoid it. M1 holds last-valid (accepted stutter); the
  predictor + optional keyframe-first coarse preview are M2 polish.
- **M1 seams that keep this cheap to add (build these in M1):** frames identified by PTS (✅ mapper); the
  controller pulls frames through a thin `IFrameSource.TryGet(pts)` (decode-only in M1, cache-backed in M2);
  the display frame is a *descriptor* that may be a GPU texture (zero-copy) **or** a native RAM buffer
  (cache) — the converter accepts either input; sequential read-ahead already lands in M1.

## VideoPlaybackEngine (global media manager — facade in Core, impl in Video)

**Foundation implemented:** the Core facade ([`Core/Video/VideoPlayback.cs`](../../Core/Video/VideoPlayback.cs)
— `IVideoPlaybackEngine` + `VideoFrameResult` + the `VideoPlayback.Engine` holder, no FFmpeg types) and the
Video impl ([`VideoPlaybackEngine.cs`](../../Video/VideoPlaybackEngine.cs) — a singleton owning per-stream
`VideoPlaybackController`s, `RequestFrame`/`ReleaseStream`, publishing itself to the Core holder on first
use). `PlayVideo` + `PlayVideoClip` are now thin clients keyed by a per-instance stream id (behavior
unchanged). The **shared cache budget** is in too: the engine divides one global 1 GB budget evenly across
live streams (a lone video gets the full 1 GB — double the old per-controller cap; the total stays bounded
as streams multiply), pushed to each `VideoFrameCache` via a settable, lazily-applied budget.
The **bounded decoder pool** is in: each stream's last-request time is stamped on `RequestFrame`, and the
engine opportunistically evicts streams that are idle past a timeout (5 s) or — when above the live cap (8) —
the most-idle ones past a short grace, freeing their decoder + worker + cache and re-dividing the budget. A
genuinely active stream is never evicted, so >cap simultaneous clips degrade by exceeding the cap rather than
thrashing decoders. So `# controllers` now tracks *near-playhead* clips, not every video op in the graph.
**Remaining:** **preroll** of upcoming clips (needs the clip player's schedule — until then an evicted clip
re-opens with a brief seek when it returns), activity-weighted budget shares, the cap/budget as project
settings, and the editor-side consumer (cache indicators).

A single global manager owns all video decode resources — the decode-stream pool, frame cache, texture pool,
the parallel-playback budget, and (later) `-optimized.mp4` proxies — in the role of `AudioEngine` /
`ResourceManager`, not operator logic. `PlayVideo` and every `VideoClip` are thin **clients** that ask it for
frames; it arbitrates the finite resources across all of them.

**Why it must be anchored in Core.** TiXL's operator load contexts are **per-package and collectible**
([`AssemblyInformation.cs`](../../Core/Compilation/AssemblyInformation.cs) creates a
`TixlAssemblyLoadContext` per package; they unload/reload). A `static` in `T3.Video` is therefore single only
*by convention* (Lib is its sole read-only loader) and would reset if that context reloaded. **Core is the
only assembly loaded exactly once and shared across every operator context + editor + player** — the same
reason `AudioEngine` lives in `Core/Audio`. So the singleton is anchored in Core.

**Split (keeps Core slim — most exports have no video):**
- **Core** holds *only*: the **`IVideoPlaybackEngine` interface** (methods use Core types — `Texture2D`,
  `Guid`, `double`, the `Optimize for` enum — **no FFmpeg types**, so Core's dependency closure gains nothing
  heavy), a **`VideoPlayback.Engine` static accessor** (null / null-object when no video assembly is loaded —
  non-video projects never touch it), and the **max-parallel-playbacks project setting**.
- **T3.Video** holds the implementation (`VideoPlaybackEngine : IVideoPlaybackEngine` + the pool, frame
  cache, texture pool, scheduler, decode backends, Sdcb). It **registers itself into the Core accessor on
  first init** (first-load-wins). FFmpeg's ~100 MB therefore still ships only with the video operator
  package — a video-less export carries none of it.

**Division of labor:** the **operator knows the graph** — a `VideoClipPlayer` scans clips and computes the
active + upcoming sets from each `TimeRange` + the playhead, and `PlayVideo` is just a single stream — and
tells the engine *what* it needs and *when*. The **engine manages the finite decode resources** (*how*): the
pool + preroll + eviction (see [`Plan_VideoClipPlayer.md`](Plan_VideoClipPlayer.md)), the GOP-cache +
realtime predictive seek (M2 above), the texture pool, and the shared budget. The engine reads the live
clock/rate from Core's `Playback`.

**Tick model:** driven by the clients' per-frame `Update` (the VCP / `PlayVideo` call `RequestFrame` /
`Preroll`) — **no central editor tick**, which is also why it works unchanged in the exported player (the
player evaluates the graph identically). Multiple VCPs, or a mix of `PlayVideo` + VCPs, all share the one
Core-held engine and its single budget.

**Budget model — start simple (1 GB).** Two *separate* constraints, both fixed constants to start:
- **RAM frame-cache budget = 1 GB** (≈ 330 1080p-NV12 frames ≈ 5.5 s @60, or ≈ 80 4K frames ≈ 1.4 s @60).
- **Live-decoder cap → project setting, default 8** — bounds threads + file handles + decoder state (which
  byte budgets don't); its own knob, not derived from cache bytes. The cap must cover *peak live = active +
  preroll + crossfade*, not the average: expected concurrency is mostly 0–1, occasionally 2–3, rarely >7, so
  8 leaves ~5 slots above the typical ≤3 active (preroll + crossfade headroom). The common case never hits
  it; the rare >7-simultaneous case falls to graceful degradation; many-layer setups raise the setting, weak
  machines lower it, and the future `Auto (% CPU/GPU)` can derive it. Lives in the same export-safe
  Video-playback project-settings group as the cache budget.
The texture/VRAM side starts minimal (output textures for active clips; pipeline A's cache is RAM, not GPU).
Fixed constants mean **no live-resize logic yet**. **Later:** an `Auto (N% RAM, M% VRAM)` / `Custom (explicit
bytes)` model — `Auto` reads total system RAM/VRAM (already queried for the About dialog), the two knobs map
to the RAM cache and the VRAM/texture pool, and changing it **reinitializes the pools** (restart/reinit), so
the simple start needs nothing dynamic.

**Settings travel with exports:** the budget is a **project setting** (Core), never an Editor `UserSettings`
value (the player has no editor); optionally clamped by a per-machine cap, combined with `min()`.

**`-optimized.mp4` proxies (later):** the engine auto-transcodes heavy sources to lightweight proxies and
substitutes them transparently — closer to an asset/`ResourceManager` concern. Depends on the deferred
**encode milestone** (needs an FFmpeg encoder).

## Build sequence (the designed engine / clip-player work)

M1 (decode + playback for `PlayVideo` / `PlayVideoClip`, software path) is **done and in use**. The
architecture above — engine, pool, cache, clip player, A/B, predictive seek — is the **next body of work**.
Recommended order, value-first and risk-managed; each step is independently shippable:

0. **Confirm the LGPL runtime swap** (editor restart) — validates the current decode foundation before
   building on it. *(User action; the only unverified piece from prior work.)*
1. **Pipeline A — seeking cache.** RAM GOP-cache + read-ahead behind an `IFrameSource` seam on the existing
   controller. Immediate scrubbing win, lowest risk, additive to the working software path. *(No engine yet.)*
2. **`VideoPlaybackEngine` + `VideoClipPlayer` (Phase 1 wired → AutoCollect).** Introduce the Core facade +
   Video impl (it now owns the step-1 cache), the `ImageComposeTransform` field + `VideoClip` /
   `TransformImage`, and the compositing player. The engine arrives here because the clip player is the first
   thing that needs the **global** pool / budget.
3. **Decode pool + preroll + eviction.** Many-clip scaling in the engine (folds into the clip player's later
   phase).
4. **Pipeline B — D3D11VA zero-copy.** The throughput path (riskiest); independent of A — slot in when
   high-res / many-stream playback is the target. Planned in detail in
   [`Plan_VideoZeroCopyDecode.md`](Plan_VideoZeroCopyDecode.md).
5. **Later:** realtime predictive seek, `-optimized.mp4` proxies (needs the encode milestone), and the
   general texture-op rollout ([`Plan_ImageComposeTransform.md`](Plan_ImageComposeTransform.md)).

*Alternative — architecture-first:* do the engine facade (step 2's Core/Video scaffolding) before step 1 so
the cache lands in the engine directly (no rework, but a refactor of working code with no immediate
user-visible win). Value-first (cache first) ships a scrubbing improvement sooner.

## Backlog / deferred (beyond M1 & M2)

- **Encode milestone — now planned in [`Plan_FfmpegEncode.md`](Plan_FfmpegEncode.md).** Replace MF
  `SinkWriter` with FFmpeg so **export works on Linux/Wine** (MF is Windows-only — the driving reason).
  Two tiers: **LGPL in-process** (the bundled decode DLLs reused) covers audio + ProRes/HAP/VP9/AV1/FFV1 +
  **hardware** H.264/HEVC (`*_nvenc`/`*_qsv`/`*_amf`); **GPL only** for *software* x264/x265, served by a
  **user-supplied `ffmpeg.exe`** (subprocess) installed via an assistant. The codec dropdown the assistant
  hangs off of does not exist yet and is added there.
- **Proxy media** — now its own plan ([`Plan_VideoProxyMedia.md`](Plan_VideoProxyMedia.md)). Auto-transcode
  long-GOP imports to a seek-friendly all-intra proxy (ProRes/HAP — never H.264, which would need GPL),
  auto-preferred at playback so large-GOP random seek gets cheap without the user thinking about codecs (the
  notes' `OptimizerService`). Downstream of the encoder (tier-1 LGPL writer); supersedes the M2 cache for a
  proxied clip. The real lever that makes random seeking fast for everyday media — a "killer feature."
- Route a video's audio track through the BASS `AudioEngine` (new push-stream `BASS_STREAMPROC`; no existing
  pattern). Lower priority.
- (Optional) Port `VideoDeviceInput` (webcam) capture to FFmpeg's **dshow** input device (`avdevice` already
  ships). Keep **DirectShowLib** for device/capability enumeration — FFmpeg's dshow listing is log-based and
  worse. The image transforms (flip/rotate/scale/resize, currently OpenCV) would move to a GPU shader/swscale.
  Buys consistency, *not* a DLL reduction (CameraCalibrator still needs OpenCV).
- Full HDR tone-mapping (PQ/HLG → linear); HAP fast-path (reuse the [DDS BCn upload](../../Core/Resource/Dds/DdsDirectX.cs));
  TiXLClip cache/bake; the media setup/install assistant.
