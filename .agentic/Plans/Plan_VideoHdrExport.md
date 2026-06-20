# HDR Video Export (PQ / HLG, 10-bit)

**Status:** Draft — 2026-06-20. Design only, no code yet. Extends the completed SDR render-export
([`archive/Plan_FfmpegEncode.md`](archive/Plan_FfmpegEncode.md) — BT.709, 8-bit) to emit HDR. Pulled out of
[`Plan_VideoFollowups.md`](Plan_VideoFollowups.md) into its own plan because the float→PQ transfer is real
design work, not a loose end.

## Goal

Export TiXL's float-rendered output as **HDR video** (HDR10 / HLG): 10-bit, BT.2020 primaries, PQ or HLG
transfer — so values >1.0 in the render become real high-dynamic-range brightness in the file, viewable on an
HDR display.

## Why it's possible — and why it doesn't work today

TiXL renders into **float textures that hold values >1.0** (super-white) — the raw material for HDR. But the
render-export **clamps it away at readback**:
[`FfmpegVideoExportWriter`](../../Editor/Gui/Windows/RenderExport/FfmpegVideoExportWriter.cs) reads each frame via
`TextureBgraReadAccess(targetFormat: R8G8B8A8_UNorm)` → **8-bit, [0,1]-clamped**, before the encoder ever sees
it. [`VideoFileEncoder`](../../VideoServices/VideoFileEncoder.cs) then does RGBA8 → BT.709 8-bit `yuv420p`.
Everything downstream is SDR, so the HDR information is already gone — *not* because of the codec, but because
of the readback format.

> Asymmetry: the **decode** side is already partly HDR-aware (`VideoDecoderSession.DetectHdr` →
> P010→RGBA16 with a tone-map *stub*). Only **encode** is SDR-only.

## The chain (every link must go HDR-aware)

1. **Higher-precision readback.** Read back as 16-bit-float / 10-bit (e.g. `R16G16B16A16_Float`) instead of
   clamped RGBA8 so >1.0 survives. The readback already runs a compute shader
   (`ConvertToCpuReadableBgra`); add an HDR target format + a 16-bit map path.
2. **10-bit codec.** `yuv420p10le` / `p010` encoder pixel format. **HEVC Main 10** is the standard HDR codec
   (`hevc_nvenc` 10-bit on NVIDIA; software `libkvazaar` 10-bit). AV1 (SVT) also supports HDR. (H.264 High 10
   exists but is non-standard for HDR — skip.)
3. **BT.2020 + PQ/HLG tagging.** Set `color_primaries = bt2020`, `colorspace = bt2020nc`,
   `color_trc = smpte2084` (PQ) or `arib-std-b67` (HLG), 10-bit, limited range — the HDR analog of the BT.709
   tags the SDR path sets today. Plus optional **HDR10 mastering metadata** (MaxCLL / MaxFALL, mastering-display
   primaries) for proper HDR10.
4. **The float→PQ transfer (the real work).** "Values above 1" isn't self-describing — PQ maps an *absolute*
   luminance range (up to 10,000 nits) into code values, so we must decide **what float 1.0 means in nits** and
   run the values through the PQ (or HLG) OETF. **swscale does not transfer-convert** (it only applies the
   YUV matrix + range), so this lives **in the readback compute shader**: the shader applies primaries + the
   PQ/HLG OETF → PQ-encoded 16-bit, and swscale then does only RGB→YUV BT.2020. *(Alternative: an FFmpeg
   `zscale`/`libplacebo` filter — rejected for now: the byte-level encoder has no filtergraph, and `libplacebo`
   may not be in the bundled LGPL build.)*

## Key design decisions (need calls / experiments on real HDR hardware)

- **Nit mapping.** Is the render scene-linear or display-referred? Propose a configurable **peak luminance**
  (e.g. 1000 nits) with float 1.0 = a reference white (e.g. 100 nits), so 10.0 → 1000 nits. Expose as a simple
  "HDR peak nits" `RenderSetting`. **HLG** is relative (no absolute nits) and is the simpler first cut.
- **Primaries.** Keep the render's 709/sRGB primaries (tag 709 primaries + PQ transfer — valid, limited gamut)
  vs. convert the gamut to BT.2020 in the shader (wider, conventional HDR10). Start simple (709 primaries + PQ),
  add the 2020 gamut conversion later.
- **PQ vs HLG default.** PQ (HDR10) is the file/broadcast standard; HLG degrades more gracefully on SDR
  displays. Offer both; default PQ.
- **Codec/profile.** HEVC Main 10 first; reuse the existing HW probe (it already lists `hevc_nvenc`).

## Settings

A new **HDR** toggle + transfer (PQ/HLG) + peak-nits on
[`RenderSettings`](../../Editor/Gui/Windows/RenderExport/RenderSettings.cs) (project `.t3ui`), mirroring the
codec selector. HDR is only valid for 10-bit-capable codecs (HEVC / AV1) — gate the UI.

## Phasing (build-verifiable)

1. **16-bit readback path.** `TextureBgraReadAccess` HDR target (`R16G16B16A16_Float`) + a 16-bit map; the
   encoder accepts a 16-bit source format. Output still SDR-tagged (just wider source pixels) — verify >1.0 is
   no longer clamped at readback.
2. **10-bit HEVC encode.** `VideoEncoderSettings` bit-depth knob; `yuv420p10le`; HEVC Main 10 (kvazaar SW for
   CI, `hevc_nvenc` for the editor). Round-trip test: encode 10-bit → decode → bit depth + frame count preserved.
3. **PQ transfer in the readback shader + BT.2020/PQ tags.** The float→PQ OETF in the compute shader; tag the
   stream. Verify with `ffprobe` (`color_trc=smpte2084`, 10-bit, `color_primaries=bt2020`) and on an HDR display.
4. **HLG + mastering metadata + UI.** HLG transfer option; HDR10 MaxCLL/MaxFALL; the `RenderSettings` HDR
   toggle + peak-nits; gate to HEVC/AV1.

## Risks / open questions

- **Nit mapping is subjective** — needs visual iteration on real HDR hardware; there's no auto "correct" value.
- **swscale doesn't transfer-convert** — the PQ/HLG OETF must live in the readback shader (or a filter we don't
  have). This couples HDR export to the GPU readback path, not just the encoder.
- **Hardware 10-bit encode** — `hevc_nvenc` 10-bit needs a capable GPU/driver (same driver-version caveat that
  bit NVENC H.264).
- **Verification needs an HDR display** — round-trip tests can check bit depth + tags but not the *look*;
  perceptual correctness is manual.
- **Gamut handling** — 709-primaries-with-PQ vs. a true BT.2020 gamut conversion is a quality/scope call.
- **SDR fallback** — most viewers are still SDR; confirm players tone-map the HDR file acceptably (HLG helps).

## Key files

| Concern | File |
|---|---|
| Readback — add 16-bit/HDR target + the PQ/HLG shader | `Core/Resource/Utils/TextureBgraReadAccess.cs` |
| Editor readback adapter — HDR pixel path | `Editor/Gui/Windows/RenderExport/FfmpegVideoExportWriter.cs` |
| Encoder — bit depth, BT.2020/PQ colorspace, 10-bit | `VideoServices/VideoFileEncoder.cs` |
| Codec→encoder mapping (HEVC Main 10) | `VideoServices/FfmpegVideoExport.cs` |
| Core facade — HDR fields on the export settings | `Core/Video/VideoExport.cs` |
| HW probe — `hevc_nvenc` 10-bit | `VideoServices/HardwareEncoderProbe.cs` |
| HDR settings (toggle / transfer / peak nits) | `Editor/Gui/Windows/RenderExport/RenderSettings.cs` |
| Decode-side HDR reference (already partly done) | `VideoServices/VideoDecoderSession.cs`, `HardwareFrameConverter.cs` |

## Manual test

Add `.tests-manual/video-hdr-export.md`: render content with values >1.0; export HEVC Main 10 PQ; `ffprobe`
shows 10-bit / `bt2020` / `smpte2084`; the file plays as HDR on an HDR display (highlights brighter than SDR
white); an SDR display tone-maps it without gross error.
