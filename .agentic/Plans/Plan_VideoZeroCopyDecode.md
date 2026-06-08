# D3D11VA GPU→GPU Zero-Copy Video Decode (Pipeline B)

**Status:** 2026-06-07 — **working end-to-end on NVIDIA, all four phases wired.** Phases 0–3 GPU-verified:
D3D11VA hardware decode on the shared `ResourceManager.Device`, NV12/P010→RGBA compute-shader convert with no
CPU read-back, correct image, smooth playback. Phase 4 wired: chosen via the `Optimize For` dropdown
(`FastSeeking` default = software decode + cache; `PlaybackPerformance` = hardware zero-copy) on
`PlayVideo`/`VideoClip` — the `TIXL_FFMPEG_*` env vars are gone. **Remaining:** in-editor verification of live mode-switching, teardown/
repeated-open stability, other GPU vendors, and the deferred refinements (BT.601 for SD, PQ/HLG HDR tone-map,
keyed-mutex fallback). The separate-decode-device "totally async" isolation (overlap NVDEC with render) is a
later, larger pass.

**Manual tests:** [`video-optimize-for-modes`](../../.tests-manual/video-optimize-for-modes.md) (the `Optimize For`
toggle, mode smoothness, 23.976 fps cadence) and [`video-playback-determinism`](../../.tests-manual/video-playback-determinism.md)
(frame-accuracy, loop/clamp, export). Implementation of **M1 step 8** of [`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md) — the hardware decode
path deferred during M1. Was the **riskiest** piece of the FFmpeg effort; the device-sharing + decode/convert
locking were the hard-won parts.

## Goal

Decode common long-GOP codecs (H.264 8-bit, HEVC 10-bit) on the GPU via **D3D11VA** and convert the decoded
NV12/P010 surface to the operator's RGBA texture **without a CPU round-trip** — the throughput path for
high-res (4K+) and many-parallel-stream playback. Vendor-neutral (AMD/Intel/NVIDIA all implement D3D11VA;
NVDEC/CUDA is deliberately avoided). GPUs lacking a codec profile fall back to the software path.

This is **Pipeline B** in the M2 A/B model ([`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md) *Milestone 2*): each
frame plays once, forward, **no RAM cache** (retaining huge once-used frames is waste). Pipeline A (software
decode + GOP cache, for seeking) already ships and is unchanged.

## Current state — what exists

- Decode is **software-only**. [`VideoDecoderSession`](../../Video/VideoDecoderSession.cs) opens the demuxer +
  decoder and decodes to a CPU `Frame` (`Nv12`/`Yuv420p`/`P010le` planes); there is **no `IDecodeBackend`, no
  hwaccel, no `get_format` hook** — the plan's backend split was designed but never built.
- [`VideoPlaybackController`](../../Video/VideoPlaybackController.cs) threading is the **inverse** of what
  zero-copy needs: the **worker thread** decodes *and* swscale-converts to packed RGBA bytes
  ([`SoftwareFrameConverter`](../../Video/SoftwareFrameConverter.cs)); the **render thread** only
  `UpdateSubresource`s those bytes into the output `Texture2D` (`UploadPendingFrame`). Handoff is a
  `lock`-guarded single-slot pending **byte buffer**.
- [`VideoPlaybackEngine`](../../Video/VideoPlaybackEngine.cs) already bounds live decoders + evicts + shares a
  cache budget — backend-agnostic, no change needed for B (B just won't populate the cache).

## What changes (the shape of the work)

Zero-copy inverts the handoff: the **worker** decodes to a **GPU `AVFrame`** (a D3D11 texture-array slice) and
hands the *frame* (ref-held) to the **render thread**, which runs a **compute shader** (NV12/P010 plane SRVs →
RGBA UAV) straight into the output texture, then unrefs. The convert **moves to the render thread** — the D3D11
immediate context must stay there. So both the session (hwaccel setup) and the controller (handoff + convert
location) need a hardware mode alongside the existing software mode.

## Hardware decode — the FFmpeg D3D11VA sequence

Reference: [`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md) *D3D11VA device sharing*. Sdcb exposes the raw API via
`Sdcb.FFmpeg.Raw.ffmpeg.*` (already used in `VideoDecoderSession` for `avcodec_flush_buffers` etc.) and the raw
hwaccel structs (`AVHWDeviceContext`, `AVD3D11VADeviceContext`, `AVHWFramesContext`, `AVD3D11VAFramesContext`).
**Step-0 task:** confirm the exact Sdcb binding surface for these structs (some are raw pointer structs).

In `VideoDecoderSession.TryOpen`, before `codecContext.Open()`, when hardware is requested:

1. **Once globally:** `ResourceManager.Device.QueryInterface<DeviceMultithread>().SetMultithreadProtected(true)`
   — required: the decoder's `ID3D11VideoContext` runs on the worker while the render thread drives the
   immediate context. (The MF encoder path already does this — mirror it; don't toggle it twice.)
2. `av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_D3D11VA)`. For the **shared-device** tier (Phase 3), set
   `AVD3D11VADeviceContext.device = ResourceManager.Device.NativePointer` and **AddRef the COM device**
   (FFmpeg `Release`s it on teardown; a refcount slip double-frees the global device → app crash). Wire
   FFmpeg's `lock`/`unlock` callbacks to a shared mutex that the converter dispatch also takes. For the
   **own-device** tier (Phases 1–2), skip setting `.device` and let FFmpeg create its own D3D11 device.
   `av_hwdevice_ctx_init`.
3. **Frames context:** allocate `hw_frames_ctx` manually so the pool textures carry
   `AVD3D11VAFramesContext.BindFlags |= D3D11_BIND_SHADER_RESOURCE` — **without this the decoder pool textures
   are decode-only and can't be wrapped in an SRV** (a classic D3D11VA-zero-copy gotcha). Set
   `codecContext.hw_device_ctx = av_buffer_ref(...)`.
4. Set the `get_format` callback to return `AV_PIX_FMT_D3D11` (fall back to software pix-fmt if absent), then
   `Open()`.
5. Decoded hw `AVFrame`: `data[0]` = `ID3D11Texture2D` **array**, `data[1]` = **array slice index**. The frame
   stays GPU-resident.

## Frame handoff rework (controller)

- Worker, hardware mode: after `TryReadNextFrame`, the `CurrentFrame` holds the GPU surface. **`av_frame_ref`**
  it into the pending slot (latest-wins: if a newer frame arrives before the render thread consumes the
  pending one, `av_frame_unref` the older). Never touch the immediate context on the worker.
- Render thread, `Update()`: take the pending GPU frame under lock, **convert on the immediate context** (next
  section), set `Texture`, then `av_frame_unref` the consumed frame. Hold the ref until the dispatch is
  submitted.
- Software mode keeps today's path verbatim (worker converts to bytes; render uploads). The pending slot
  becomes a small tagged union: *RGBA bytes* (sw) **or** *GPU `AVFrame`* (hw).
- Export (`renderingToFile`) blocking is unchanged in shape — `WaitForRequestedFrame` still gates on the
  worker producing the target; only the payload type differs.

## GPU converter + shader (the zero-copy convert)

New HLSL compute shader under `Operators/Lib/Assets/shaders/img/` (cs_5_0, loaded via
`ResourceManager.CreateShaderResource<ComputeShader>`), dispatched C#-side on the render thread following the
save/restore pattern in
[`TextureBgraReadAccess.cs:235-265`](../../Core/Resource/Utils/TextureBgraReadAccess.cs) (get
`deviceContext.ComputeShader`, save prev shader/SRV/UAV, set, `Dispatch(gx,gy,1)` with 16×16 groups, restore).

- **Inputs:** two SRVs off the decoder texture array slice — **luma** (`R8_UNorm` for NV12 / `R16_UNorm` for
  P010) and **chroma** (`R8G8_UNorm` / `R16G16_UNorm`), each an explicit
  `ShaderResourceViewDescription` with `Dimension = Texture2DArray`, `FirstArraySlice = (int)data[1]`,
  `ArraySize = 1`. `SrvManager.GetSrvForTexture` only builds a default non-arrayed SRV, so these are built
  explicitly. **Cache SRVs by slice index** (FFmpeg reuses ~20 slices); invalidate on size/format change.
- **Output:** the operator's RGBA texture as a UAV — `Rgba8` (SDR) / `Rgba16_Float` (HDR).
- **Math:** YUV→RGB with the matrix selected from the session's color space (BT.601 / 709 / 2020). HDR
  (P010, PQ/HLG) targets RGBA16 with a **tone-map stub** in M1 (washed colors acceptable; full PQ/HLG is
  backlog).

## Fallback tiers (auto-degrade, software always wins last)

1. **Shared device** (primary, Phase 3): true zero-copy, one device, no cross-device copy.
2. **Own device + keyed-mutex shared texture** (Phases 1–2, and a permanent fallback): FFmpeg owns its D3D11
   device, decode there, copy the slice into a `Shared`/`KeyedMutex` texture, open it on
   `ResourceManager.Device`, convert. One GPU→GPU copy, still no CPU round-trip. Reuses the same converter +
   controller.
3. **Software** (guaranteed baseline, already shipped): if hwaccel init fails or the GPU lacks the codec
   profile, fall back transparently.

Detection: `av_hwdevice_ctx_init` failure, `get_format` not offering `AV_PIX_FMT_D3D11`, or
`avcodec_open2` failure → fall back. Log the chosen tier once.

## `Optimize for` operator parameter (the A/B selector)

Add **only once both backends exist** (don't ship a dead parameter) — Phase 4. An `enum { FastSeeking,
PlaybackPerformance }` (default **FastSeeking**) input on `PlayVideo` and `VideoClip`:

- `FastSeeking` → Pipeline A (software decode + RAM GOP-cache — today's path).
- `PlaybackPerformance` → Pipeline B (D3D11VA zero-copy, **no cache**), falling back to **software-without-cache**
  if hwaccel init fails.

The enum expresses *intent*; the controller picks the best available backend and **re-inits if the value
changes at runtime**. Codec can override (HAP/intra-GPU is always GPU — out of scope here). Only `FastSeeking`
draws on the shared cache budget, simplifying the budget story.

## Packaging

D3D11VA needs **no extra DLLs** — it uses the OS D3D11 runtime and the hwaccel built into avcodec/avutil. **But
the shipped `FFmpeg.LGPL` BtbN build must have `--enable-d3d11va`** (license-clean, not gpl/nonfree, so the
guardrail is unaffected — but presence must be confirmed). **Phase-0 verification:** at runtime log
`av_hwdevice_iterate_types` / scan the cached `avcodec_configuration()` for `d3d11va`. The broad BtbN
"everything" build almost certainly includes it; confirm before building on it.

## Phasing (each step independently build- and GPU-verifiable; riskiest last)

0. **Backend seam + packaging probe. — DONE (2026-06-07).** Packaging probe passed: `avcodec-61.dll` ships
   `h264_d3d11va` + `d3d11va_alloc_context` + `d3d11vaframescontext`, `avutil-59.dll` ships the `d3d11va`
   hwcontext, and the **Sdcb managed binding exposes** `av_hwdevice_ctx_alloc`/`_init`,
   `av_hwframe_transfer_data`, `av_buffer_ref`, `get_format`, and the `AVD3D11VADeviceContext` /
   `AVD3D11VAFramesContext` structs. The actual backend *seam* is thinner than first assumed and lands with
   Phase 1 (the controller handoff stays byte-based until Phase 2), so there is no standalone refactor here.
1. **Hardware decode + readback.** D3D11VA on FFmpeg's **own** device (sidesteps the global-device
   refcount-crash hazard); set `get_format → AV_PIX_FMT_D3D11`, decode to GPU, then `av_hwframe_transfer_data`
   down to CPU and reuse the **existing** `SoftwareFrameConverter`. **No `SHADER_RESOURCE` BindFlags needed** —
   readback doesn't use SRVs, so FFmpeg's default frames context suffices (the BindFlags gotcha is Phase 2
   only). Gate behind a temporary force-flag (env var, mirroring `TIXL_FFMPEG_ALLOW_RESTRICTED`) until the
   `Optimize for` param lands in Phase 4. Proves hardware **decode** with zero new-shader/device-sharing risk.
   *Verify: hardware decode engages (log hwaccel / pixfmt), frames correct, plays.*
   **DONE — verified on GPU (2026-06-07):** `D3D11VA hardware (CPU read-back) — 2048x1080 Nv12` on an H.264
   clip; frames correct. Contained entirely in `VideoDecoderSession` (own-device
   `av_hwdevice_ctx_create` + a static `get_format` returning `D3d11` + per-frame `av_hwframe_transfer_data`
   read-back; `CurrentFrame`/`PixelFormat` report the CPU/NV12 result, so the controller/converter/cache are
   untouched). Gated by `TIXL_FFMPEG_FORCE_HW=1`; unset = byte-for-byte the old software path (no regression
   risk). `VideoPlaybackController` logs `Video decode path: D3D11VA hardware (CPU read-back) | software …`.
   Every Sdcb interop signature compiled first try. **Still slower than zero-copy** — there's a GPU→CPU
   read-back each frame; the speed win is Phase 2.
2. **GPU convert (zero-copy convert).** New NV12/P010→RGBA compute shader + the array-slice SRV dispatch into
   the output UAV — no readback. *Verify: identical frames, no CPU round-trip, faster; H.264 8-bit (NV12) and
   HEVC 10-bit (P010→RGBA16, stub tone-map).* **In progress (2026-06-07):** four parts — (a) the
   `Nv12ToRgba-cs.hlsl` BT.709 shader is **written**; (b) **code done (pending verify)** — `get_format` now
   allocates the frames context via `avcodec_get_hw_frames_parameters` and ORs `SHADER_RESOURCE` into
   `AVD3D11VAFramesContext.BindFlags`, falling back to the decode-only pool (read-back still works) if a driver
   rejects it; logs `SHADER_RESOURCE frames context ready` vs the fallback; (c) **code done (compiles, untested)** —
   `HardwareFrameConverter` wraps the decoder texture (balanced `Marshal.AddRef` so FFmpeg's ref is untouched —
   a flagged risk spot), builds R8/R8G8 `Texture2DArray`-slice SRVs, and dispatches `Nv12ToRgba-cs.hlsl` into a
   UAV output, restoring + releasing the prior compute-stage bindings; (d) **code done (compiles, untested)** — the
   worker→render handoff: in zero-copy the session skips `av_hwframe_transfer_data` and exposes the GPU
   `AVFrame`; the worker `av_frame_ref`s it into a single latest-wins slot (no cache, no swscale); the render
   thread `av_frame_move_ref`s it out under `_lock` and runs `HardwareFrameConverter.Convert` on the immediate
   context outside the lock. Only ~2 pool slices pinned at once. Gated behind `TIXL_FFMPEG_ZEROCOPY=1` (with
   `FORCE_HW`); the verified read-back path stays the fallback.
   **(c)+(d) DONE — verified on GPU (2026-06-07):** correct image, no frame drops, smooth (no read-back). The
   COM-refcount on the decoder texture turned out fine (balanced); the real blocker was decode/convert racing
   on the shared device — fixed by the lock callbacks in Phase 3 below.
   **P010 / 10-bit (2026-06-07):** a real 10-bit HEVC file (`...2160p.10bit...P010`) first crashed natively — the
   converter built an `R8` SRV on a `P010` (R16) surface and D3D11 threw `E_INVALIDARG` in
   `CreateShaderResourceView`. Now supported: `HardwareFrameConverter` reads the decoder texture's DXGI format and
   binds R16/R16G16 plane SRVs + an RGBA16 output for P010/P016 (R8/R8G8 + RGBA8 for NV12). The BT.709 shader is
   unchanged — both planes sample as normalized floats, and the 10-bit limited-range end points differ <0.5% from
   8-bit, so the same constants serve both. `SupportsZeroCopy` admits 4:2:0 8/10/12-bit (it reads the bitstream
   bit depth since `sw_pix_fmt` isn't set until the first decode); `HardwareSurfaceFormat` labels the read-back
   `PixelFormat`/cache correctly (P010le, was defaulting to Nv12). **Still a stub for true HDR:** PQ/HLG (BT.2020)
   P010 decodes but the BT.709 matrix + no tone-map leaves it washed — full PQ/HLG is the remaining HDR item.
   bt709 10-bit (the common UHD-SDR case) is correct.
   **Seek policy — surfaced by zero-copy having no cache (2026-06-07):** real long-GOP 4K (`~250-frame / ~10 s`
   GOPs) soft-locked when jump-seeking during playback — playback time ran ahead, every catch-up frame tripped the
   old 0.5 s sequential threshold, and `DecodeTo` seeked *back* to the keyframe and re-decoded the whole GOP each
   frame (~100 ms/frame, never converging). The cache hid this on the software path. Fix in
   `VideoPlaybackController.DecodeTo`: seek only when the target is **behind** the decoder or **beyond a
   forward-seek threshold** (a forward target inside the current GOP decodes forward — far cheaper, and it
   converges since decode at ~52 fps outruns 24 fps playback). The threshold is **adaptive** — it grows to the
   deepest observed keyframe→target span, learning the stream's GOP depth (~10 s here) so within-GOP catch-up
   never re-seeks, while genuine jumps past a GOP still seek. Verified with 4 concurrent same-file decoders all
   converging to steady `seq` after a jump. Seek *latency* (one GOP grind) is unchanged — that's the cache's job
   (Phase 4 *Fast Seeking* mode).
   **Cache-key mismatch — Fast Seeking jitter (2026-06-07):** with the toggle's *Fast Seeking* (software + cache)
   as default, a 23.976 fps clip stuttered badly while 60 fps clips were fine. A per-second probe showed every
   displayed frame was a fresh decode (`decoded == published`) with decode time ramping 22→340 ms then resetting —
   a backward-seek GOP re-decode every frame. Cause: the cache stored frames under their **raw decoded PTS** but
   the render thread looked them up by the **frame-grid-snapped target** (`SecondsToFramePts`); at fractional
   frame rates those differ by ~1 ms (target 41 vs PTS 42), so *every* lookup missed, and since prefetch had run
   the decoder ahead, the forced `DecodeTo` saw a negative delta → backward seek → GOP re-decode. 60 fps masked it
   (snapped target and PTS coincide). Fix: `VideoPlaybackController.FrameKey(pts)` maps a decoded PTS back through
   the same snapping, so cache writes and the render-side lookup use one key. Probe-verified: steady playback went
   to `published 25/s, decoded 0, maxDecode 0.0 ms` (forward frames now all cache hits); spikes remain only during
   genuine hard seeks (the GOP grind), as expected.
3. **Shared device (full zero-copy).** Swap FFmpeg's own device for `ResourceManager.Device` with the
   AddRef-careful sharing + `SetMultithreadProtected` + lock/unlock callbacks; eliminate the cross-device copy.
   Keep tier 2 (own-device + keyed-mutex) as the fallback. *Verify: stable across AMD/Intel/NVIDIA; **no
   double-free crash on dispose**; clean teardown under repeated open/close.*
   **Phasing correction (2026-06-07):** this is **not** an optional last-copy optimization — Phase 2's GPU
   convert *requires* it. Decoding on a separate FFmpeg device left the surfaces on the wrong device, and
   `CreateShaderResourceView` on a cross-device resource **crashed natively** on the first convert. So Phase 3
   was pulled forward into Phase 2. **DONE — verified on GPU (2026-06-07):** `TryOpenHardware` uses
   `av_hwdevice_ctx_alloc` → set `AVD3D11VADeviceContext.device = ResourceManager.Device.NativePointer`
   (single `Marshal.AddRef`, balanced by FFmpeg's teardown `Release`; init-failure path releases it) →
   `av_hwdevice_ctx_init`.
   **Locking — the hard-won fix:** leaving `lock`/`unlock` null did **not** make FFmpeg install its own
   ID3D11Multithread locking, so decode (worker) and convert (render) raced on the shared device — the decoder
   failed, FFmpeg re-initialised the hwaccel, repeat, until it gave up (a tell-tale loop of repeated
   `SHADER_RESOURCE frames context ready` with a changing texture pointer, freezing after a non-deterministic
   17–50 frames). Fix: **explicit `lock`/`unlock` callbacks** that take a shared managed lock
   (`HardwareFrameConverter.DeviceLock`), which the converter's dispatch also takes, plus an explicit
   `SetMultithreadProtected(true)`. With that, decode and convert are mutually exclusive and it's stable.
   The keyed-mutex tier-2 fallback is not built (only needed if a GPU refuses device sharing). **Still untested:**
   teardown under repeated open/close (the device AddRef/Release balance) and other GPU vendors (NVIDIA verified).
4. **`Optimize for` param + auto-fallback.** The enum on `PlayVideo`/`VideoClip`, A/B selection, runtime
   re-init on change, graceful fallback to software on hwaccel failure. *Verify: switching the param swaps
   pipelines live; forcing hwaccel failure degrades to software without a stall or error state.*
   **DONE — wired (2026-06-07):** `VideoPlaybackOptimization { FastSeeking = 0 (default), PlaybackPerformance }`
   in `Core/Video`, surfaced as the `OptimizeFor` dropdown on `PlayVideo` and `VideoClip` (C#-only — TiXL
   auto-reconciles `.t3`/`.t3ui`). Threaded operator → `IVideoPlaybackEngine.RequestFrame` → `controller.Update`
   → `OpenSource` → `VideoDecoderSession.TryOpen(url, optimization, ...)`. `FastSeeking` decodes in **software** with
   the RAM cache — no per-frame GPU read-back stall, so playback stays smooth and the GPU is free for the editor
   (a hardware read-back default was tried first and was visibly jittery even at 720p). `PlaybackPerformance`
   decodes **zero-copy on the GPU** (falls back to software if the profile is unsupported). The
   `TIXL_FFMPEG_FORCE_HW` / `_ZEROCOPY` env vars are **removed**. The stream re-opens when the mode
   changes (`mode != _workerMode` in `ProcessLatestRequest`). `VideoStreamInput` (live) passes `FastSeeking`;
   29 Video.Tests pass (they fall back to software — no D3D device in the test host).
   **Mode-switch teardown — two bugs found + fixed (2026-06-08):** switching Fast Seeking ↔ Playback Performance
   (especially with a resolution change) bricked the op. (1) On re-open the worker dropped the pending GPU frame
   under `_lock` so the render thread can't convert a frame from a just-disposed session, and the zero-copy convert
   is wrapped in try/catch (keeps the last texture instead of surfacing an operator error). (2) The real brick: the
   controller's `Texture` and the converter's `_output` were the **same object**, but the software path's
   `UploadPendingFrame` disposed `Texture` on a size change — freeing the converter's output behind its back, so the
   next `EnsureOutput` read a dead texture's `Description` and threw "COM object null" permanently. Fix: the software
   path owns a **separate `_softwareTexture`**; the converter exclusively owns/disposes `_output`. Invariant: never
   let two owners dispose the output texture. **Still to verify in-editor:** the `Optimize For` dropdown
   label/position (user repositions in the editor, which regenerates the `.t3`/`.t3ui`).

**Riskiest = Phase 3** (global-device lifetime). Fallbacks in order: own-device + keyed-mutex → software.
Phases 0–2 already deliver a working hardware decode (with a single GPU→GPU copy at worst); Phase 3 is the
last-copy optimization.

## Risks & mitigations

- **Global-device double-free (crash).** A refcount slip on the shared `ResourceManager.Device` double-frees
  it. *Mitigation:* own-device for Phases 1–2; shared-device only in Phase 3, with explicit AddRef and a
  teardown test (repeated open/close). Tier-2 fallback never touches the global device's refcount.
- **Decoder pool textures decode-only (no SRV).** *Mitigation:* manual `hw_frames_ctx` with
  `BindFlags |= SHADER_RESOURCE` (Phase 1).
- **Threading (`ID3D11VideoContext` on worker + immediate context on render thread).** *Mitigation:*
  `SetMultithreadProtected(true)` + FFmpeg lock/unlock callbacks serialize device access; the converter
  dispatch takes the same mutex.
- **Per-GPU codec-profile gaps.** *Mitigation:* auto-fallback to software (Phase 4); log the tier.
- **Render-thread convert cost.** One fullscreen compute dispatch per displayed frame — cheap, and the worker
  no longer runs swscale (net offload). Threshold-cull not needed.
- **Unverifiable by the author.** Every phase needs user GPU testing; budget a log-probe round on hwaccel init
  (Phase 1) and the shader/SRV slice (Phase 2).

## Open questions

1. ~~Does `FFmpeg.LGPL 20250329.1.0` ship D3D11VA?~~ **Resolved (Phase 0):** yes — `h264_d3d11va` and the
   d3d11va hwcontext are in the shipped DLLs.
2. ~~Sdcb binding surface for the hwaccel structs.~~ **Resolved (Phase 0):** `Sdcb.FFmpeg` exposes
   `av_hwdevice_ctx_alloc`/`_init`, `av_hwframe_transfer_data`, `av_buffer_ref`, `get_format`, and the
   `AVD3D11VADeviceContext` / `AVD3D11VAFramesContext` structs. Exact `get_format`-callback mechanism (managed
   delegate vs. raw function pointer) is the one detail to nail down when Phase 1 coding starts.
3. **Color-matrix selection** — does the session already expose `ColorSpace`/`ColorRange` (it detects HDR via
   `ColorTrc`/pixfmt)? Thread BT.601/709/2020 + full/limited range into the shader.
4. **SRV cache invalidation** — by slice index is enough while the pool is stable; confirm FFmpeg doesn't
   reallocate the array mid-stream (size/format change → rebuild).
5. **Target GPUs for verification** — which vendors/codecs to validate first.
