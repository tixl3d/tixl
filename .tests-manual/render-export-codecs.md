---
id: render-export-codecs
title: Render Export — Video Codecs
added: 2026-06-19
added-in-version: 4.2
scope: render-export
tags: [essential]
prerequisites:
  - A project is open with an operator that has a Texture2D output selected or pinned in the Output Window (so "Render To File" can render it).
  - For the audio step, the project has a soundtrack on the timeline.
---

Covers the video **Codec** selector in the "Render To File" window. The editor encodes via the bundled
LGPL FFmpeg build — **all in-process, no external ffmpeg, no GPL**: **H.264** and **HEVC** (`.mp4`,
hardware-accelerated where available, else software OpenH264 / libkvazaar), **ProRes** (`.mov`), **VP9**
and **AV1** (`.mp4`, software delivery codecs), **FFV1** (`.mkv`, lossless), and **HAP ×3** (`.mov`). All carry
AAC audio. The verification for each codec is a round-trip: render a short range, then re-import the file with a
`[PlayVideo]` operator (or an external player) and confirm it decodes.

## Step: The Codec dropdown lists all codecs with friendly labels

**Action:**
Open the **Render To File** window. With **Render Mode** set to **Video**, find the **Codec** dropdown near
the top of the video settings and open it.

**Expected:**
- A **Codec** dropdown is shown, with **H.264** selected by default.
- Opening it lists: **H.264**, **HEVC (H.265)**, **ProRes**, **VP9**, **AV1**, **FFV1**, **Hap**, **Hap
  Alpha**, **Hap Q** — note the friendly labels (e.g. "Hap Alpha", not "HapAlpha").

## Step: The filename extension tracks the codec

**Action:**
Note the **Filename** field (e.g. `render-v01.mp4`). Change **Codec** to **ProRes**, then to **FFV1**, then
back to **H264**, watching the filename each time.

**Expected:**
- The extension follows the codec: ProRes → `.mov`, FFV1 → `.mkv`, VP9/AV1/H264 → `.mp4`.
- The base name (e.g. `render-v01`) is unchanged — only the extension swaps.

## Step: Bitrate shows only for the rate-controlled codecs

**Action:**
Switch **Codec** through the options and watch for the **Bitrate** control and its quality hint.

**Expected:**
- **Bitrate** (and the "… quality (Est. … MB)" hint) appear for **H.264**, **HEVC**, **VP9**, and **AV1**.
- They are hidden for **ProRes** and **FFV1** (those set their own rate / are lossless); HAP shows its own
  fixed-size estimate instead.

## Step: The codec dropdown shows an inline encoder indicator

**Action:**
With **Render Mode** set to **Video**, watch the small line just below the **Codec** dropdown while
switching the codec. Try **H264** first, then a non-H.264 codec (e.g. **ProRes**).

**Expected:**
- A single muted line appears under the dropdown (it may briefly read "Checking encoder…" the first time).
- On a machine with a working GPU encoder, **H264** shows **"Hardware encoder (NVIDIA NVENC / Intel Quick
  Sync / AMD AMF)"** matching the GPU. (NVENC in the bundled build needs NVIDIA driver 570+; Intel Quick Sync
  works via `nv12`.)
- On a machine with **no** working hardware encoder, **H264** shows **"Software encoder"** — it encodes
  in-process with **OpenH264** (no GPL, no external ffmpeg).
- Non-H.264 codecs (ProRes/VP9/AV1/FFV1/HAP) show **"Software encoder"** too.
- Switching codecs swaps the line in place without shifting the rest of the panel.
- **No codec ever asks for an external ffmpeg or a download** — everything encodes in-process.

## Step: H.264 export produces a playable MP4 with audio

**Action:**
Set **Codec** to **H264**, choose a short range (a few seconds), make sure **Export Audio** is on, and press
**Start Render**. When it finishes, re-import the written file with a `[PlayVideo]` operator (or open it in an
external player).

**Expected:**
- A `.mp4` file is written to the chosen folder.
- It plays back with the correct image and audible, in-sync audio.

## Step: HEVC export produces a playable MP4

**Action:**
Set **Codec** to **HEVC (H.265)**, choose a short range, press **Start Render**, then re-import with
`[PlayVideo]`.

**Expected:**
- A `.mp4` file is written and plays back with the correct image. The indicator reads **"Hardware encoder
  (…)"** on a machine with HEVC hardware support, else **"Software encoder"** (libkvazaar — software HEVC is
  slow, so expect a slower render).
- No external ffmpeg / no popup. (Note: HEVC-in-MP4 from a software encode may not preview in Windows
  Explorer / QuickTime without the `hvc1` tag, but plays in `[PlayVideo]` and VLC.)

## Step: ProRes export produces a playable MOV

**Action:**
Set **Codec** to **ProRes**, press **Start Render**, then re-import the output with `[PlayVideo]`.

**Expected:**
- A `.mov` file is written and plays back with the correct image.
- The file is noticeably larger than the H.264 render of the same range.

## Step: VP9, AV1, and FFV1 each export and re-import

**Action:**
For each of **VP9**, **AV1**, and **FFV1** in turn: keep the range short, press **Start Render**, then
re-import the output with `[PlayVideo]`. VP9 and AV1 are software-encoded, so expect a slower render than
H.264 — especially at high resolution.

**Expected:**
- Each writes its file (`.mp4` for VP9/AV1, `.mkv` for FFV1) and plays back with the correct image.
- FFV1 is visually lossless and its file is much larger than the others.
- No red operator-error state on the `[PlayVideo]` re-import, and no editor freeze during the render.

## Step: HAP exports in-process with a size estimate

HAP encodes **in-process** with the bundled FFmpeg (the build includes the `hap` encoder) — no external
ffmpeg, no download.

**Action:**
Select **Hap** (also try **HapAlpha** / **HapQ**), keep the range short, **Start Render**, then re-import the
output with `[PlayVideo]`.

**Expected:**
- The three HAP entries appear; the filename extension becomes `.mov`; the inline line reads **"Software
  encoder"** and **Start Render** is enabled — no popup, no external ffmpeg.
- A size estimate appears (e.g. **"Est. 1.9 GB (1120×932, DXT before Snappy)"**) — HAP is a fixed-ratio codec,
  so the prediction is reliable; HapAlpha/HapQ are ~2× Hap. The dimensions shown are **rounded down to a
  multiple of 4** (HAP's DXT block size), and the summary card shows the same rounded size, not the raw one.
- A `.mov` is written and plays back with the correct image (HapAlpha preserves alpha; HapQ is higher
  quality / larger). With **Export Audio** on, it carries an AAC track.

## Step: Codec choice survives save and reload

**Action:**
Set **Codec** to **AV1**, save the project, then close and reopen it (or reload). Open the **Render To File**
window again.

**Expected:**
- **Codec** is still **AV1**, and the **Filename** still ends in `.mp4`.
