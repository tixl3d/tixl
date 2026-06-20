# FFmpeg Video Encoding (replacing Media Foundation export)

**Status:** In progress — 2026-06-20. Render-export is **fully in-process** via the bundled LGPL FFmpeg — no
external ffmpeg, no GPL. Working & tested: the encoder core (video + AAC audio), the cross-ALC bridge, eager
registration, the codec selector (H.264 + **OpenH264** software fallback, **HEVC** via hardware/libkvazaar,
ProRes, VP9, AV1, FFV1, **HAP** ×3 — friendly dropdown labels), the inline availability indicator
(hardware / software), HAP size estimate + ×4 rounding, and the hardware-encoder fixes (QSV `nv12`; NVENC needs
NVIDIA driver 570+). **Media Foundation encode is fully removed (Phase 5 done).** The render-export milestone
is essentially complete — FFmpeg is the only encode path; MF decode was already gone.

> **⚠ Major correction (supersedes the tier-2 / GPL design below).** The original plan assumed the bundled
> build couldn't encode HAP or software H.264 in-process, requiring a "tier-2" external GPL `ffmpeg.exe` + an
> install assistant. **That premise was wrong** — it came from probing the *test project's* Sdcb-nuget FFmpeg
> DLLs, not the editor's actual **BtbN** build. Verified against the real shipped DLLs (swapped in, probed,
> restored): the editor's build **has the `hap` encoder** (HAP encodes in-process — confirmed by the render log
> *"using the in-process FFmpeg encoder"*) and **`libopenh264`** (Cisco's **BSD** software H.264 — *not* GPL)
> **and `libkvazaar`** (software HEVC). Only `libx264`/`libx265` are genuinely absent (GPL). So:
> - **HAP** → in-process `Software`. Never needed external ffmpeg.
> - **Software H.264** (no hardware encoder) → **OpenH264 in-process** (BSD, far better than the old MPEG-4
>   fallback), no GPL, no download. (Licensing: BSD-openh264 + LGPL-FFmpeg compose cleanly under MIT; GPL is the
>   *only* thing that would collide with MIT, and it's avoided. H.264 *patents* are a separate axis GPL wouldn't
>   have solved either — hardware encoders are vendor-covered.)
> - **The entire tier-2 path was removed** (subprocess writer, resolver, setup dialog, `UserSettings.ExternalFfmpegPath`,
>   the `External`/`SoftwareFallback` availability kinds). The sections below describing it are **historical**.
>
> A future "prefer libx264/libx265 max quality" option *would* need external GPL ffmpeg — but that's a niche
> opt-in, not the default, and isn't built.

The **encode milestone** deferred by
[`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md) (which replaced MF *decode* with FFmpeg but left *encode* on
Media Foundation).

## Goal

Replace the Media Foundation `SinkWriter` render-export path with FFmpeg, so that:

1. **Export works on Linux/Wine.** MF is Windows-only and does not run under Wine — today an exported
   project cannot render video at all off Windows. FFmpeg is the cross-platform encoder. *This is the
   driving reason, not codec breadth.*
2. **TiXL stays MIT.** We ship only the **LGPL** FFmpeg build (already bundled for decode) and never
   distribute a GPL encoder. ~~The one corner that legally needs GPL — *software* H.264/HEVC — is served by a
   user-supplied `ffmpeg.exe` via an assistant.~~ **Superseded:** software H.264 is served in-process by
   **OpenH264** (BSD) and HEVC by **libkvazaar**, both already in the bundled build — no GPL, no external exe.
3. **Parity first.** The current default (H.264/MP4 + AAC, project soundtrack muxed in) keeps working,
   byte-for-byte equivalent in user-visible behaviour, before any new codecs are exposed.

**Locked decision: Media Foundation encode is removed entirely** — no Windows-only fallback — once tier-1
reaches parity. MF decode is already gone (the encoder is its last user), so this also drops the
`SharpDX.MediaFoundation` package from `Core`. The deliberate consequence: a Windows machine with **no
hardware encoder and no GPL exe** can no longer software-encode H.264/HEVC via MF — it must use a hardware
encoder, pick an LGPL codec, or install the GPL exe (the fallback order below).

## The licensing reframe (the load-bearing insight)

The GPL gate is **codec-specific, and narrow**:

- **Audio never needs GPL.** Native **AAC** and **FLAC** (and MP3 via LAME) are in the LGPL build. The
  exported audio is TiXL's rendered mixdown, not a source file's track (see *Current state*), so the audio
  side is licence-clean regardless of video codec.
- **Most video codecs never need GPL.** ProRes, DNxHD, VP9, AV1, FFV1, and **HAP** all have native or
  LGPL-class encoders in the build.
- **Hardware H.264/HEVC never needs GPL.** `h264_nvenc`/`hevc_nvenc` (NVIDIA), `h264_qsv`/`hevc_qsv`
  (Intel Quick Sync, incl. most Intel iGPUs), `h264_amf`/`hevc_amf` (AMD) are LGPL wrappers shipped in the
  build; on Linux NVENC + VAAPI. Most desktops can hardware-encode H.264.
- **Only `libx264` / `libx265` (software H.264/HEVC) are GPL.** FFmpeg has *no* native H.264/HEVC encoder.

So the GPL build is needed in exactly one situation:

> **Software H.264 or HEVC, on a machine with no usable hardware encoder — or when the user deliberately
> prefers software x264/x265 quality over the hardware encoder.**

Everything else exports with the bundled LGPL FFmpeg, no download, no popup.

## Current state — what exists (verified)

- **No codec choice today.** [`RenderSettings`](../../Editor/Gui/Windows/RenderExport/RenderSettings.cs)
  has `RenderMode {Video, ImageSequence}`, `Bitrate` (default 25 Mbit), `ExportAudio` (bool), `FrameRate`,
  `ResolutionFactor` — but **no video codec/container selector**. Video export is hardcoded H.264/MP4 via
  the MF [`MfVideoWriter`](../../Editor/Gui/Windows/RenderExport/MF/MfVideoWriter.cs) (input format locked to
  `R8G8B8A8_UNorm`). The codec dropdown the install-assistant hangs off of **does not exist yet — this
  milestone adds it.**
- **`RenderSettings` persists per-project in `.t3ui`** (`WriteToJson`/`ReadFromJson` → `SymbolUi.RenderSettings`).
  So the codec *intent* travels with the project; an install *path* must not (see *Settings*).
- **Audio export already exists and stays unchanged in shape.**
  [`RenderProcess.ComputeAudioBufferForVideoFrame`](../../Editor/Gui/Windows/RenderExport/RenderProcess.cs)
  pulls `AudioRendering.GetFullMixDownBuffer(1/FrameRate)` — the project's full BASS mixdown as float PCM —
  per frame, and hands it to the writer next to each video frame
  ([`MFAudioWriter`](../../Editor/Gui/Windows/RenderExport/MF/MFAudioWriter.cs) encodes AAC/MP3/FLAC, MF
  muxes). The FFmpeg writer consumes the *same* mixdown buffer.
- **Decode already ships FFmpeg LGPL.** `FFmpeg.LGPL 20250329.1.0` (BtbN `lgpl-shared`, avcodec-61 /
  FFmpeg 7.x) lands flat next to `Lib.dll` in the operator package; `FfmpegLibrary`'s guardrail rejects
  GPL/non-free builds. The **encode tier-1 path reuses these very DLLs** (no new bundled binaries).

## Two-tier architecture

The decode→convert core is irrelevant here; what matters is **where the encoder runs**, dictated by licence:

### Tier 1 — LGPL, in-process (the default; no download, no popup)
Reuse the **already-bundled** LGPL FFmpeg via the in-process `Sdcb.FFmpeg` bindings (the same path decode
uses). Covers: **all audio**; **ProRes / DNxHD / VP9 / AV1 / FFV1**; **hardware H.264/HEVC**
(`*_nvenc`/`*_qsv`/`*_amf`). A new `FfmpegVideoWriter` mirrors the `MfVideoWriter` contract
(`ProcessFrames(Texture2D, ref byte[] audio, channels, sampleRate)` + `Dispose`), reads the BGRA/RGBA
texture back (reuse `TextureBgraReadAccess`), feeds video + the mixdown PCM into libav, and muxes.

> **HAP is *not* tier-1**, despite the early assumption above. The shipped BtbN `lgpl-shared` build ships the
> HAP *decoder* but **no HAP encoder** (verified — see Risks), so HAP encode falls to tier 2. It is **not** a
> licence problem (HAP isn't GPL) — the encoder is simply absent from our build.

### Tier 2 — out-of-process `ffmpeg.exe` (software x264/x265 — *and* HAP)
A user-supplied / system **`ffmpeg.exe`** invoked as a **subprocess**: raw BGRA frames piped on stdin, raw
PCM on a second input, `ffmpeg` does video + audio + mux. **Two distinct reasons a codec lands here:**

- **Licence (software H.264/HEVC):** `libx264`/`libx265` are GPL and we won't distribute them. The process
  boundary is the FFmpeg-recommended way to keep TiXL MIT while the user runs their own GPL binary — TiXL
  distributes nothing GPL. **Needs a GPL build.**
- **Missing encoder (HAP):** the encoder just isn't in our LGPL build. **Any** external ffmpeg that has the
  `hap` encoder serves it — a system/LGPL build is fine; **no GPL involved.** (HAP variants map to
  `-c:v hap -format hap|hap_alpha|hap_q`; dimensions must be a multiple of 4.)

Chosen over an in-process build deliberately:

- **ABI decoupling.** Decode is painfully pinned to avcodec-61/FFmpeg-7.x to match the Sdcb bindings (a
  `.0330` bump to avcodec-62 already broke it). Swapping the bundled DLLs just to gain the hap encoder would
  risk that pin. The **CLI is stable across majors**, so the exe is immune.
- **Linux/Wine.** On Linux the user almost certainly has `/usr/bin/ffmpeg` already — a subprocess uses it
  for free. (Especially relevant where Wine blocks hardware encode *and* MF: system `ffmpeg` is the answer.)
- **Crash isolation.** A bad user-supplied binary fails the export, not the editor.

**Per-codec encoder requirement.** The resolver must check the located ffmpeg actually *has* the encoder the
codec needs (`ffmpeg -hide_banner -h encoder=hap` / `=libx264`), not just that an exe exists — a stock system
ffmpeg has `hap` but may lack `libx264`, and vice-versa. Cache the per-encoder result like the HW probe.

**Audio follows the tier** — whichever tier encodes the video also encodes/muxes the audio, so there is
never a cross-process audio handoff.

### Assembly placement (verified during implementation — corrects the original key-files assumption)

The **Editor does not reference `Video.csproj`** (only Core/Logging/MsForms/SilkWindows), and `Sdcb.FFmpeg`
lives in `Video` inside the *operator* load context. So the encoder **cannot** be an Editor type as first
assumed — it lives in **`Video`** and the Editor reaches it through a **Core facade**, exactly like the
existing `IVideoPlaybackEngine` / `VideoPlayback.Engine` holder in
[`Core/Video/VideoPlayback.cs`](../../Core/Video/VideoPlayback.cs).

- **Encoder core — done & tested.** [`VideoServices/VideoFileEncoder.cs`](../../VideoServices/VideoFileEncoder.cs) is a
  self-contained, **CPU-byte-level** encoder (RGBA bytes in → muxed file out; the GPU texture read-back
  stays in the caller, so it unit-tests without a D3D device). Codec is caller-chosen
  (`h264_nvenc`/`_qsv`/`_amf` for the editor; `mpeg4` for CI). Verified by
  [`VideoServices.Tests/VideoFileEncoderTests.cs`](../../VideoServices.Tests/VideoFileEncoderTests.cs): encode 30 synthetic
  frames → decode back → correct size + frame count.
- **Eager registration — deferred to the Video package extraction
  ([`Plan_VideoOperatorPackage.md`](Plan_VideoOperatorPackage.md)).** `VideoPlayback.Engine` is published
  lazily on *first video-op use*; encode can't inherit that (render-export may run with no prior playback). A
  first cut put a `[ModuleInitializer]` in **Lib** to force `Video.dll` to load+register eagerly — **rejected
  and removed**: it couples FFmpeg's load to Lib's hot-reload path (the most-edited package, behind most
  real-world load/reload exceptions) for a <1% feature. The right home is the new **`Video` operator package**:
  referencing it loads it → it registers `FfmpegVideoEncoderFactory.Register()` into the Core
  `VideoExport.Factory` holder, covering the no-video-op case without touching Lib. Core is loaded once and
  shared across the editor and operator load contexts, so the single holder bridges them. *Until that lands,
  `VideoExport.Factory` is simply unset (the impl + Register entry exist in `FfmpegVideoExport.cs`).*

## The codec selector + fallback order (new UI)

Add a **codec/container selector** to `RenderSettings` (project-persisted, alongside `Bitrate`/`ExportAudio`).
A one-shot, cached **hardware-encoder probe** (ask the bundled LGPL `ffmpeg` to open `h264_nvenc`/`qsv`/`amf`
— *availability in the build ≠ a working GPU*) decides what's silently available. Resolution order for a
requested codec:

1. Codec is LGPL-native (ProRes/VP9/AV1/FFV1/audio) → **tier 1**, always.
2. H.264/HEVC + a hardware encoder initialises → **tier 1** (HW), silent.
3. H.264/HEVC + no HW + GPL `ffmpeg.exe` located → **tier 2**, silent.
4. H.264/HEVC + no HW + no GPL → **the install assistant** (below).
5. **HAP** + an external `ffmpeg.exe` with the `hap` encoder located → **tier 2**, silent (no GPL needed).
6. **HAP** + no such ffmpeg → **the external-ffmpeg assistant** (same flow as 4, different copy — *missing
   encoder*, not *licence*). A located GPL build that also has `hap` satisfies both 4 and 5.

The current build (Phase 3) implements 1, 2, and the "unsatisfiable" half of 4/6: `GetAvailability` reports
`Unavailable` for HAP, the inline indicator says so, and export is gated. Phase 4 adds the tier-2 subprocess
and turns those gates into the located/assistant paths.

UI surfacing — **don't fire a modal on dropdown change** (twiddling to compare options shouldn't nag):
- **On selection:** a quiet inline indicator next to the dropdown — `Hardware encoder` tag when HW serves
  it; a ⚠ `Software H.264 needs an extra component — [Set up]` line in case 4; a ⚠ `HAP needs an external
  FFmpeg — [Set up]` line in case 6. Optionally grey-tag the dropdown option itself only when nothing can
  serve it. *(Phase 3 already draws these warning lines; Phase 4 wires the `[Set up]` action.)*
- **Modal only at commitment:** the user clicks `[Set up]`, *or* hits **Export** with an unsatisfiable
  codec.

## External-ffmpeg assistant (tier-2)

Triggered by case 4 (software H.264/HEVC, no HW) **or** case 6 (HAP, no ffmpeg with the encoder). One flow,
two entry copies — the difference is *why* an external ffmpeg is needed, and HAP must **not** imply GPL:

- **Case 4 (software H.264/HEVC):** "…we can't bundle the **GPL** build (licence) — install it yourself."
  The download offer points at the BtbN **gpl** zip.
- **Case 6 (HAP):** "…the bundled FFmpeg doesn't include the **HAP** encoder — point TiXL at an FFmpeg that
  does." **No GPL framing.** A system ffmpeg or any non-GPL build with `hap` is fine; the download offer can
  point at a permissive (lgpl/full) build. If a GPL build is already present and has `hap`, reuse it.

It's **`ffmpeg.exe`** (not a `.dll`), and the BtbN builds are a **zip, not an installer**.

```
┌─ Video encoding needs an extra component ──────────────┐
│  «case 4» TiXL can encode software H.264/HEVC with      │
│  FFmpeg, but can't bundle the GPL build (licence).      │
│  «case 6» HAP needs an FFmpeg build that includes the   │
│  HAP encoder, which the bundled one doesn't.            │
│  Install/point to one yourself, once.  [ Set up FFmpeg ]│
└─────────────────────────────────────────────────────────┘
  on click → a checklist that advances live:
  ✓ 1. Open the download page (BtbN ffmpeg-*.zip)        → [Open page]
  ◐ 2. Pick the downloaded zip / an existing ffmpeg.exe    [Browse…]
        (auto-found in Downloads; zip verified by SHA-256)
  ○ 3. TiXL extracts/records ffmpeg.exe in its tools folder
  ○ 4. Verify it has the needed encoder: -h encoder=hap|libx264 ✓
                                            [ Done ] [ Cancel ]
```

Detection precedence (only show the popup if all miss): TiXL-recorded exe in AppData → system `ffmpeg` on
`PATH` (covers Linux/Wine and Windows users who already have it) → offer the download. **The verify step is
per-encoder**, not just "an exe exists" (a system ffmpeg may have `hap` but not `libx264`). The pinned
SHA-256 over an official BtbN release is what makes auto-picking a Downloads file safe; the install target is
a persistent **AppData** tools folder (not the app dir — read-only on some installs, wiped on update),
remembered across sessions.

## Settings — where each value lives

| Value | Home | Why |
|---|---|---|
| **Codec / container choice** | `RenderSettings` (Core-adjacent, **project** `.t3ui`) | Travels with the project — a project that renders ProRes should remember it. Mirrors existing `Bitrate`/`ExportAudio`. |
| **External `ffmpeg.exe` path** | `UserSettings.ConfigData` (**Editor, per-machine**) | Export is editor-only (the Player never encodes) → not Core. A per-machine install path **must not** ride exports → not a project setting. The mirror image of the decode budget, which *is* a Core project setting because the player plays back. **One path, not "the GPL path"** — the same exe serves both software H.264/HEVC (GPL build) and HAP (any build with `hap`); the resolver checks per-encoder, not by licence. |
| **Override** | env `TIXL_FFMPEG_EXE` | Power-user / CI, mirroring the existing `TIXL_FFMPEG_ALLOW_RESTRICTED` precedent. Resolver order: UserSettings path → env → `PATH`. |

```csharp
// UserSettings.ConfigData
/// Path to a user-supplied external FFmpeg (ffmpeg.exe) for codecs the bundled LGPL build can't encode:
/// software H.264/HEVC (a GPL build) and HAP (any build that includes the hap encoder). The resolver verifies
/// the *specific* encoder is present, so this single path can satisfy either. Per-machine; not shipped with
/// projects. Empty = not configured / not located yet.
public string? ExternalFfmpegPath = null;
```

**Two separate FFmpeg installs.** This path is *only* the external encode exe (tier-2). Decode resolves its
own bundled LGPL DLLs flat next to `Lib.dll` and needs no path — don't conflate them in UI or naming. *(Note
for [`Plan_InstallVerificationAndSafeStartup.md`](Plan_InstallVerificationAndSafeStartup.md): the external exe
is an **optional, user-supplied** component — it must **not** appear in the install-verifier manifest, which
covers TiXL's own shipped files. The bundled LGPL decode DLLs do belong in the manifest; the external encoder
does not.)*

## Audio (intersection with Plan_VideoAudio)

The FFmpeg writer encodes the **same `GetFullMixDownBuffer` PCM** the MF path does — no new audio work for
this milestone. [`Plan_VideoAudio.md`](Plan_VideoAudio.md) routes a *decoded video's* audio track into that
mixdown (its Phase 3, deterministic export); once that lands, video audio is encoded **for free** by this
writer, with no encode-side change. The two plans meet at the export mixdown; the encoder choice (MF vs
FFmpeg) is orthogonal to audio *routing*.

## Phasing (build-verifiable; `dotnet build` after each step)

1. **Tier-1 parity writer** — split into:
   - **1a. Encoder core (video) — DONE & TESTED.** [`VideoServices/VideoFileEncoder.cs`](../../VideoServices/VideoFileEncoder.cs)
     (RGBA→YUV swscale at the codec's `EncoderPixelFormat` → caller-chosen codec → mux) + round-trip unit tests
     (MPEG-4 + ProRes).
   - **1b-i. Hardware-encoder selection — DONE & TESTED.**
     [`VideoServices/HardwareEncoderProbe.cs`](../../VideoServices/HardwareEncoderProbe.cs) opens each candidate
     (`h264_nvenc`→`_qsv`→`_amf`) to find the one that actually works on this GPU; the caller falls to an LGPL
     software codec when none does. Verified live (NVENC H.264 round-trip on an NVIDIA dev machine; `_qsv`/
     `_amf` correctly reported unavailable). Reused by Phase 3's dropdown-availability UI.
   - **1b-ii. Audio — DONE & TESTED.** AAC stream from the interleaved-float mixdown PCM (manual
     deinterleave to planar + buffering to AAC's fixed 1024-sample frames; stereo/48k→stereo/48k needs no
     resample). Verified: a video+audio render muxes a readable AAC stream. Full `Video.Tests` suite 32/32.
   - **1c-i. Cross-ALC bridge — DONE & BUILD-VERIFIED.** Core facade
     ([`Core/Video/VideoExport.cs`](../../Core/Video/VideoExport.cs): `IVideoFileWriter` /
     `IVideoEncoderFactory` / `VideoExport.Factory`, all FFmpeg-free, byte-level) + the Video impl
     ([`VideoServices/FfmpegVideoExport.cs`](../../VideoServices/FfmpegVideoExport.cs) wrapping `VideoFileEncoder`, picking
     the HW encoder via the probe with an MPEG-4 LGPL fallback). Core + Video build. **Registration** (who
     calls `Register()`) moves to the new `Video` operator package — see
     [`Plan_VideoOperatorPackage.md`](Plan_VideoOperatorPackage.md); the interim Lib trigger was removed.
   - **1c-ii. Editor readback adapter + `RenderProcess` wiring — DONE (build-verified; in-editor verify
     pending).** [`FfmpegVideoExportWriter`](../../Editor/Gui/Windows/RenderExport/FfmpegVideoExportWriter.cs)
     reads each output frame back via `TextureBgraReadAccess`'s **synchronous `useImmediateReadback`** mode
     (`ConvertToCpuReadableBgra` — compute-shader convert of any source format straight to **RGBA8**, then map),
     and feeds the bytes + the audio mixdown to the Core `IVideoFileWriter`. No async/one-frame-delay/drain
     needed (export is offline). A shared `IRenderVideoWriter` interface lets it and `MfVideoWriter` coexist;
     `RenderProcess` prefers FFmpeg (via `VideoExport.Factory`) and **falls back to MF** when the factory isn't
     registered (so nothing regresses during the transition). Registration is a `[ModuleInitializer]` in the
     **Video operator package** ([`VideoExportRegistration.cs`](../../Operators/Video/VideoExportRegistration.cs)).
     Editor builds. *In-editor verify: a render produces a playable FFmpeg file (H.264 via the HW probe);
     audio muxed + aligned; `ExportAudio=false` → silent; MF fallback engages when FFmpeg is absent.*
     **Eager registration — CLOSED (build-verified).** The `[ModuleInitializer]` otherwise fires only on first
     Video-package code execution, so an export from a project that never touched a video op would find the
     factory unset. Fix: when `TryCreate` sees a null factory it calls a new
     [`AssemblyInformation.RunModuleInitializers()`](../../Core/Compilation/AssemblyInformation.cs)
     (`RuntimeHelpers.RunModuleConstructor` — idempotent, no-op for packages without one, exception-guarded) on
     every loaded package, firing the Video package's initializer so the encoder registers without any video
     op, then re-checks (MF only if the package genuinely isn't loaded). Runs once per session (only while the
     factory is unset) and **doesn't touch the package-load flow**.
2. **Codec/container selector — DONE & TESTED (H.264, ProRes, VP9, AV1, FFV1).** `VideoExportCodec` enum in the
   Core facade ([`Core/Video/VideoExport.cs`](../../Core/Video/VideoExport.cs)) with a `GetFileExtension` helper;
   project-persisted `RenderSettings.VideoCodec` (string-enum) + a "Codec" dropdown in
   [`RenderWindow`](../../Editor/Gui/Windows/RenderExport/RenderWindow.cs). The factory's `BuildEncoderSettings`
   switches on it: **H.264** → HW probe / MPEG-4 fallback at `Yuv420p` (`.mp4`); **ProRes 422** →
   `AVCodecID.Prores` at `Yuv422p10le` (`.mov`); **VP9** → `libvpx-vp9` (`.mp4`); **AV1** → `libsvtav1` (`.mp4`);
   **FFV1** → `ffv1`, lossless (`.mkv`) — all at `Yuv420p` with AAC audio (no Opus rework needed, since VP9/AV1
   mux into `.mp4`). The encoder carries an explicit `EncoderPixelFormat` (was a hardcoded 4:2:0), since ProRes
   needs 4:2:2. UI: the filename extension tracks the codec (`.mp4`/`.mov`/`.mkv`); Bitrate shows only for the
   rate-controlled codecs (H.264/VP9/AV1), hidden for ProRes/FFV1. Round-trip unit tests green for all five.
   Codec availability **verified against the shipped LGPL `avcodec-61.dll`** (config grep:
   libvpx/libaom/libsvtav1/libopus present; libx264/libx265 absent).
   **HAP — wired but tier-2-gated.** The three variants (`Hap`/`HapAlpha`/`HapQ` → encoder `hap` with the
   `format` private option, RGBA in, dimensions rounded to a multiple of 4) and a generic codec-private-option
   mechanism (`VideoEncoderSettings.VideoCodecOptions`, applied via `av_opt_set`/`AV_OPT_SEARCH.Children`) are
   implemented and round-trip-tested. **But the bundled build has no HAP encoder** (decode-only — see Risks), so
   `GetAvailability` reports HAP `Unavailable`, the render window shows a "needs an external FFmpeg" indicator,
   and **export is gated** (H.264 stays exempt — it always has a fallback path). HAP lights up once Phase 4's
   out-of-process `ffmpeg.exe` lands; the round-trip tests skip until an encoder-capable build is present.
   The block-size constraint (×4) is centralised in `VideoExportCodec.RoundToEncoderBlock` (Core), used by the
   encoder, the summary-card resolution, **and a HAP file-size estimate** (fixed-ratio DXT: Hap 0.5 B/px,
   HapAlpha/HapQ 1 B/px — so the prediction is reliable and the displayed dims match what's written).
   **Still to add:** DNxHR (needs a profile + pixel-format knob), animated **GIF** (deferred — single-pass quality
   is poor; wants a palettegen/paletteuse two-pass), and webm/Opus delivery (container-aware audio codec).
   *Verify (in-editor): each available codec renders a playable file via `[PlayVideo]` re-import; HAP is listed
   but gated; choice survives save/reload — see [`render-export-codecs`](../../.tests-manual/render-export-codecs.md).*
3. **Hardware probe + inline availability UI — DONE.** The cached HW probe (Phase 1b-i) is surfaced through the
   Core facade as `IVideoEncoderFactory.GetAvailability(codec)` → `VideoEncoderAvailability {Kind, EncoderName}`
   with just **`Hardware` / `Software` / `Unavailable`** (the tier-2-era `SoftwareFallback`/`External` kinds were
   removed). The Video impl ([`FfmpegVideoExport.cs`](../../VideoServices/FfmpegVideoExport.cs)) maps H.264 to
   `Hardware` (friendly GPU name) or `Software` (OpenH264); every other codec to `Software` when its encoder is
   present, else `Unavailable`. The editor probes off the UI thread and caches per codec
   ([`VideoEncoderAvailabilityCache.cs`](../../Editor/Gui/Windows/RenderExport/VideoEncoderAvailabilityCache.cs)
   — the GPU-encoder open must not stall a draw frame), and `RenderWindow` draws a constant-footprint inline
   line under the Codec dropdown (checkmark + "Hardware encoder (…)" / "Software encoder", or a ⚠ for the rare
   `Unavailable`, which also gates export). *Verify: H.264 reads "Hardware encoder (NVIDIA NVENC / Intel Quick
   Sync)" where present, else "Software encoder" — see [`render-export-codecs`](../../.tests-manual/render-export-codecs.md).*
4. **~~Tier-2 external `ffmpeg.exe` + assistant~~ — BUILT, THEN REMOVED (see the correction banner up top).**
   The whole tier-2 path (subprocess writer, resolver, setup dialog, `UserSettings.ExternalFfmpegPath`, the
   `External`/`SoftwareFallback` kinds, the routing) was built to serve HAP and software H.264 out-of-process —
   then deleted once it turned out the **bundled build encodes both in-process** (HAP via `hap`, software H.264
   via **OpenH264**). What survived from this work and **ships**:
   - **OpenH264 software H.264.** No-hardware H.264 now uses **`libopenh264` in-process** (BSD, no GPL), with
     MPEG-4 only as a last resort if a build lacks it. Replaces the old MPEG-4 fallback. `GetAvailability`
     returns `Software` (EncoderName "OpenH264"); round-trip-tested.
   - **Hardware-probe pixel-format fix (hardware-verified).** The probe and encode used `yuv420p` for *every*
     hardware encoder, but `h264_qsv` only accepts `nv12` — so **Intel Quick Sync could never open**. Both now
     use `HardwareEncoderProbe.EncoderInputFormat` (`nv12` for `*_qsv`, else `yuv420p`). Verified on real Intel
     hardware. Separately, **NVENC in the bundled BtbN build needs NVIDIA driver 570.0+** (nvenc API 13.0).
   - **HAP size estimate + ×4 rounding** (`VideoExportCodec.RoundToEncoderBlock`, in Core).
   A future **"prefer libx264/libx265 max quality"** opt-in *would* reintroduce an external-GPL path — recover
   the deleted tier-2 code from git if it's ever wanted. Not planned.
5. **Remove MF encode entirely — DONE.** Deleted the `Editor/Gui/Windows/RenderExport/MF/` folder
   (`MfVideoWriter`, `MFAudioWriter`, `MFHelper`, `FormatConversion`) and dropped the `SharpDX.MediaFoundation`
   package reference from [`Core.csproj`](../../Core/Core.csproj). `RenderProcess` no longer falls back to MF —
   if the FFmpeg writer can't be created it fails the export with a clear message. The warmup-frame-skip
   constant (`MfVideoWriter.SkipImages`) moved into `RenderProcess` as `WarmupFramesToSkip`. Core/Editor/Player
   build clean; `grep` for `MediaFoundation`/`SinkWriter`/`MediaFactory` is empty in code.
   *In-editor verify: export still works for every codec (it's the only path now).*

**Later (own milestones):** **proxy media** — auto-transcode heavy imported media to a seek-friendly
all-intra proxy, auto-preferred on load — is its own plan
([`Plan_VideoProxyMedia.md`](Plan_VideoProxyMedia.md)) and is **downstream of this encoder** (a proxy is a
transcode, so it reuses the tier-1 LGPL writer and can't ship before it). HDR encode (PQ/HLG) and a "prefer
software quality" toggle are polish.

## Risks / open questions

- **Wine + hardware encode.** If Wine blocks `*_nvenc`/`*_qsv` *and* MF is gone, a Linux/Wine user wanting
  H.264 specifically falls to the GPL exe — which on Linux is a one-line `apt install ffmpeg`. The
  system-`ffmpeg` detection makes this painless; worth validating it actually engages under Wine.
- **Pinned-build codec presence.** Verified by grepping the shipped LGPL `avcodec-61.dll` configuration
  string: `--enable-libvpx` / `--enable-libaom` / `--enable-libsvtav1` / `--enable-libopus` /
  `--enable-libvorbis` / `--enable-libsnappy` / `--enable-libmp3lame` are **present**;
  `--enable-libx264` / `--enable-libx265` are **absent** (the GPL premise holds). Native AAC/FLAC/ProRes/FFV1
  are built in. **HAP: decode-only in this build.** Despite `--enable-libsnappy`, the BtbN `lgpl-shared`
  build ships the HAP *decoder* but **no HAP encoder** (`Codec.FindEncoderByName("hap")` → null; `FindEncoderById`
  → "codec id Hap not found"). So tier-1 in-process HAP encode is **not possible with the shipped DLLs** — HAP
  must go through the tier-2 out-of-process `ffmpeg.exe` (Phase 4). HAP is **not GPL**, so any external ffmpeg
  with the hap encoder (incl. a system LGPL one) serves it — it doesn't require the GPL build. `GetAvailability`
  now probes real encoder presence, so an absent encoder reports `Unavailable` (and the render window gates
  export) rather than failing mid-render.
- **Pipe throughput (tier 2).** Raw BGRA at 4K60 is ~2 GB/s over the pipe — fine locally, but confirm no
  stall vs. letting `ffmpeg` read frames; consider `nv12`/`yuv420p` rawvideo to cut bandwidth.
- **HW encoder quality.** HW H.264 trades efficiency for speed vs x264 at equal bitrate — some users will
  *want* the GPL software path even with a GPU. The selector should make "software (best quality)" an
  explicit, GPL-gated choice, not only a no-HW fallback.
- **Colour/range fidelity.** Match MF's bt709 / limited-range tagging so existing renders don't shift.
- **D3D device sharing for HW encode.** `*_qsv`/`*_nvenc` in-process may want the global device's
  multithread-protected context (as the decode/MF paths already set) — see the device-sharing notes in
  [`Plan_VideoZeroCopyDecode.md`](Plan_VideoZeroCopyDecode.md); the encoder reads the render output texture,
  so confirm no contention with the immediate context.

## Key files

| Concern | File |
|---|---|
| Render driver (per-frame texture + audio → writer) | `Editor/Gui/Windows/RenderExport/RenderProcess.cs` |
| Render settings (codec selector — DONE; project `.t3ui`) | `Editor/Gui/Windows/RenderExport/RenderSettings.cs` |
| Render window UI (codec dropdown + inline availability — DONE) | `Editor/Gui/Windows/RenderExport/RenderWindow.cs` |
| Off-thread encoder-availability probe + cache (DONE) | `Editor/Gui/Windows/RenderExport/VideoEncoderAvailabilityCache.cs` |
| FFmpeg encoder core (DONE — video + AAC audio) | `VideoServices/VideoFileEncoder.cs` |
| Hardware-encoder probe (DONE) | `VideoServices/HardwareEncoderProbe.cs` |
| Encoder round-trip + HW tests (DONE) | `VideoServices.Tests/VideoFileEncoderTests.cs` |
| Core encode facade (DONE) | `Core/Video/VideoExport.cs` |
| Video factory + writer impl (DONE) | `VideoServices/FfmpegVideoExport.cs` |
| Eager registration | moves to the `Video` package — see [`Plan_VideoOperatorPackage.md`](Plan_VideoOperatorPackage.md) |
| Editor readback adapter + render driver (DONE) | `Editor/Gui/Windows/RenderExport/RenderProcess.cs` + `FfmpegVideoExportWriter.cs` |
| Mixdown PCM source (reused unchanged) | `Editor/Gui/Windows/RenderExport/RenderProcess.cs` + `Core/Audio/AudioRendering.cs` |
| Hardware-encoder probe + per-encoder pixel format (DONE) | `VideoServices/HardwareEncoderProbe.cs` |
| Editor availability cache (DONE) | `Editor/Gui/Windows/RenderExport/VideoEncoderAvailabilityCache.cs` |
| Bundled LGPL FFmpeg + guardrail | `VideoServices/FfmpegLibrary.cs` |
| Texture readback for CPU encode | `Core/Resource/Utils/TextureBgraReadAccess.cs` |

## Manual test set

Covered by [`render-export-codecs`](../../.tests-manual/render-export-codecs.md): each codec (H.264, ProRes,
VP9, AV1, FFV1, HAP ×3) renders a playable file via `[PlayVideo]` re-import — **all in-process, no popup, no
external ffmpeg**; the codec choice survives save/reload; the H.264 indicator reads "Hardware encoder (…)"
where a GPU encoder works, else "Software encoder" (OpenH264). Still worth adding for the broader milestone:
a **Wine/Linux** render producing a playable file (the original driving reason — FFmpeg encode where MF can't run).
