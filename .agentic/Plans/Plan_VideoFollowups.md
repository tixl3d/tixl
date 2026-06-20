# Video Follow-ups (loose ends from the completed video milestones)

**Status:** Open backlog — 2026-06-20. A grab-bag of small open ends left behind as the big video milestones
landed: FFmpeg **decode** (M1), **encode**, the **`Video` operator package** extraction, and **D3D11VA
zero-copy** decode are all done and **archived** under [`archive/`](archive/). This collects what they each
deferred so the loose ends live in one place instead of five "done-but-not-quite" plans.

**Not here (still their own plans — real features / milestones, not loose ends):**
[`Plan_VideoAudio.md`](Plan_VideoAudio.md) (video audio → BASS),
[`Plan_VideoProxyMedia.md`](Plan_VideoProxyMedia.md) (background proxy transcode — now unblocked, since it
reuses the finished encoder), and [`Plan_VideoHdrExport.md`](Plan_VideoHdrExport.md) (HDR / PQ / 10-bit export).

**Not yet reviewed** (out of this pass; flag for a later consolidation): `Plan_VideoClipPlayer.md` (many-clip
decode pool, referenced by the decode plan's M2) and `Plan_VideoDeviceInput.md`.

---

## From the encode milestone ([archive/Plan_FfmpegEncode.md](archive/Plan_FfmpegEncode.md) — DONE)

Render-export is fully in-process via the bundled LGPL FFmpeg (H.264/OpenH264, HEVC/libkvazaar, ProRes, VP9,
AV1, FFV1, HAP ×3). Deferred polish:

- [ ] **Wine/Linux render test** — the *original driving reason* for the whole milestone (MF couldn't run
      off-Windows). Validate a real export produces a playable file under Wine/Linux.
- [x] **Colour-primaries / range fidelity (BT.709) — DONE.** `VideoFileEncoder` now tags the stream
      `bt709` primaries/trc/colorspace + `mpeg` (limited) range, **and** drives a raw `SwsContext` with
      `sws_setColorspaceDetails(ITU709)` so the RGBA→YUV matrix is actually 709 (swscale defaults to 601;
      Sdcb's `VideoFrameConverter` couldn't set it). Both halves verified by test (tag = bt709/limited; pure-green
      Y = 709 value, not 601). *In-editor: A/B vs an old render to confirm the shift is gone.* (HDR/BT.2020 future.)
- [ ] **Async / off-thread export encode** — the export loop runs on the main thread, so a slow per-frame
      encode (or a heavy graph) **freezes the UI** for the whole render. VP9/AV1 were sped up (row-mt, presets,
      `ThreadCount=0`) which mitigates it, but the real fix is to keep the GPU read-back on the render thread and
      hand the bytes to a background encoder queue. Larger change; would also make the progress UI responsive.
- [ ] **HEVC `hvc1` stream tag** — software-HEVC-in-MP4 may not preview in Windows Explorer / QuickTime
      without it (plays fine in `[PlayVideo]`/VLC). Set the stream `codec_tag` in `VideoFileEncoder`.
- **HDR export (PQ/HLG, 10-bit)** → moved to its own plan: [`Plan_VideoHdrExport.md`](Plan_VideoHdrExport.md).
      (Not a loose end — the float→PQ transfer is real design work.)
- [ ] **DNxHR codec** — needs a profile + pixel-format knob (the encoder already carries `EncoderPixelFormat`).
- [ ] **Animated GIF** — needs a `palettegen`/`paletteuse` two-pass (frame buffering) for acceptable quality;
      single-pass looks poor. Deferred deliberately.
- [ ] **webm / Opus delivery** — container-aware audio codec (Opus for `.webm` instead of AAC).
- [ ] **"Prefer libx264/libx265 max quality" opt-in** — a niche external-GPL path. The tier-2 subprocess
      writer + resolver + setup dialog were built then **removed** (openh264/kvazaar cover software in-process);
      recover them from git if this is ever wanted. Not planned.

## From the `Video` operator package ([archive/Plan_VideoOperatorPackage.md](archive/Plan_VideoOperatorPackage.md) — ~DONE)

The `Video` package exists, FFmpeg ops + natives moved out of Lib, registration lives in the package.

- [ ] **In-editor verify** — editor loads the `Video` package; ops resolve cross-package by GUID; a video
      plays; FFmpeg natives load from the `Video` output; a project with no `Video` dependency ships no FFmpeg.
- [ ] **Existing-user-project migration** (the one non-trivial bit) — projects using video ops need
      `<Operators Include="Video"/>` or the GUIDs won't resolve. Decide: auto-add on load vs a "missing package"
      prompt. (First-party Examples/template already updated.)
- [ ] **Move `SwiftCamDevice` (+ swiftcam.dll) into `Video`** — a later phase.
- [ ] **OpenCV camera ops** (`VideoDeviceInput`, `CameraCalibrator`) — blocked: Lib only sheds EmguCV once
      `OnvifCamera` (PTZ) and `Video2DPointScanner` (DMX) also move/drop OpenCV. Separate decision; not "video".

## From the decode milestone — M2 caching ([archive/Plan_FfmpegVideo.md](archive/Plan_FfmpegVideo.md) — M1 DONE, M2 partial)

The per-controller RAM frame cache + forward read-ahead ship (the default **Fast Seeking** path). Remaining
M2 items (incremental optimizations on a working cache):

- [ ] **Reverse read-behind / reverse playback** — the most feature-like of these; pull out into its own plan
      if it becomes a focused effort.
- [ ] **GOP-band cache eviction** — current is pure-recency LRU; GOP-aware eviction suits long-GOP H.264/HEVC.
- [ ] **Engine-centralized shared cache budget** — today it's 512 MB *per controller*; centralize across clips.
- [ ] **`Optimize for` cache on/off gating** — only `Fast Seeking` clips should draw on the shared budget.
- [ ] **`PlayVideoClip` in-editor verify** — timeline scrub + render-to-file (decode step 7 was "code done,
      pending verify").
- [ ] **`VideoStreamInput` live-RTSP verify** — the OpenCV→FFmpeg RTSP port (step 9 was "pending live verify").

## From zero-copy decode ([archive/Plan_VideoZeroCopyDecode.md](archive/Plan_VideoZeroCopyDecode.md) — DONE on NVIDIA)

D3D11VA GPU→GPU zero-copy ships as the **Playback Performance** mode of the `Optimize For` dropdown. Remaining:

- [ ] **In-editor verify of live mode-switching** (`Fast Seeking` ↔ `Playback Performance` at runtime) and
      teardown / repeated-open stability.
- [ ] **Other GPU vendors** — verified on NVIDIA; check Intel / AMD.
- [ ] **Colour/HDR refinements** — BT.601 for SD content; PQ/HLG HDR tone-map (currently a stub).
- [ ] **Keyed-mutex fallback** tier (shared-texture path) when own-device decode isn't available.
- [ ] **"Totally async" separate-decode-device isolation** — overlap NVDEC with render on its own device; a
      later, larger pass (not a small loose end — promote to its own plan if pursued).

## Manual tests already covering parts of the above

`render-export-codecs.md` (encode), `video-playback-determinism.md` (decode), `video-optimize-for-modes.md`
(zero-copy / Optimize-For). New loose-end work should extend these rather than add new sets where they fit.
