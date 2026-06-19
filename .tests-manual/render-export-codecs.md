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
LGPL FFmpeg build: **H.264** (`.mp4`, hardware-accelerated where available), **ProRes** (`.mov`), **VP9**
and **AV1** (`.mp4`, software-encoded delivery codecs), and **FFV1** (`.mkv`, lossless). All carry AAC audio.
The verification for each codec is a round-trip: render a short range, then re-import the file with a
`[PlayVideo]` operator (or an external player) and confirm it decodes.

## Step: The Codec dropdown lists all five codecs

**Action:**
Open the **Render To File** window. With **Render Mode** set to **Video**, find the **Codec** dropdown near
the top of the video settings and open it.

**Expected:**
- A **Codec** dropdown is shown, with **H264** selected by default.
- Opening it lists exactly: **H264**, **ProRes**, **VP9**, **AV1**, **FFV1**.

## Step: The filename extension tracks the codec

**Action:**
Note the **Filename** field (e.g. `render-v01.mp4`). Change **Codec** to **ProRes**, then to **FFV1**, then
back to **H264**, watching the filename each time.

**Expected:**
- The extension follows the codec: ProRes → `.mov`, FFV1 → `.mkv`, VP9/AV1/H264 → `.mp4`.
- The base name (e.g. `render-v01`) is unchanged — only the extension swaps.

## Step: Bitrate shows only for the rate-controlled codecs

**Action:**
Switch **Codec** through all five options and watch for the **Bitrate** control and its quality hint.

**Expected:**
- **Bitrate** (and the "… quality (Est. … MB)" hint) appear for **H264**, **VP9**, and **AV1**.
- They are hidden for **ProRes** and **FFV1** (those set their own rate / are lossless).

## Step: The codec dropdown shows an inline encoder indicator

**Action:**
With **Render Mode** set to **Video**, watch the small line just below the **Codec** dropdown while
switching the codec. Try **H264** first, then a non-H.264 codec (e.g. **ProRes**).

**Expected:**
- A single muted line appears under the dropdown (it may briefly read "Checking encoder…" the first time).
- On a machine with a working GPU encoder, **H264** shows **"Hardware encoder (NVIDIA NVENC / Intel Quick
  Sync / AMD AMF)"** matching the GPU. (NVENC in the bundled build needs NVIDIA driver 570+; Intel Quick Sync
  works via `nv12`.)
- On a machine with **no** working hardware encoder but an `ffmpeg` with `libx264` on PATH, **H264** shows
  **"External FFmpeg encoder"** (it will software-encode x264 rather than MPEG-4).
- On a machine with **neither**, **H264** shows a ⚠ line: **"No hardware H.264 encoder — using MPEG-4. Update
  the GPU driver or install FFmpeg for x264."**
- Non-H.264 codecs (ProRes/VP9/AV1/FFV1) show **"Software encoder"**.
- Switching codecs swaps the line in place without shifting the rest of the panel.

## Step: H.264 export produces a playable MP4 with audio

**Action:**
Set **Codec** to **H264**, choose a short range (a few seconds), make sure **Export Audio** is on, and press
**Start Render**. When it finishes, re-import the written file with a `[PlayVideo]` operator (or open it in an
external player).

**Expected:**
- A `.mp4` file is written to the chosen folder.
- It plays back with the correct image and audible, in-sync audio.

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

## Step: HAP encodes via an external FFmpeg (tier 2)

The bundled LGPL build ships the HAP *decoder* but no HAP *encoder*, so HAP export runs an external
`ffmpeg.exe` (any build with the `hap` encoder — a system ffmpeg is fine; no GPL needed). It's located from
`UserSettings.ExternalFfmpegPath` → the `TIXL_FFMPEG_EXE` env var → `ffmpeg` on `PATH`.

**Action (machine *with* a hap-capable ffmpeg on PATH):**
Select **Hap** (also try **HapAlpha** / **HapQ**), keep the range short, **Start Render**, then re-import the
output with `[PlayVideo]`.

**Expected:**
- The three HAP entries appear; the filename extension becomes `.mov`.
- The inline line reads **"External FFmpeg encoder"** (not a warning), and **Start Render** is enabled.
- A `.mov` is written and plays back with the correct image (HapAlpha preserves alpha; HapQ is higher
  quality / larger). With **Export Audio** on, it carries an AAC track.

**Action (machine *without* a hap-capable ffmpeg):**
Select **Hap** with no `ffmpeg` on `PATH`/env/setting.

**Expected:**
- The inline line shows a ⚠ **"HAP encoding needs an external FFmpeg (not available yet)."** and **Start
  Render** is disabled for HAP — it does **not** silently render a different codec.

## Step: Codec choice survives save and reload

**Action:**
Set **Codec** to **AV1**, save the project, then close and reopen it (or reload). Open the **Render To File**
window again.

**Expected:**
- **Codec** is still **AV1**, and the **Filename** still ends in `.mp4`.
