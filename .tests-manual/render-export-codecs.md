---
id: render-export-codecs
title: Render Export — Video Codecs
added: 2026-06-19
added-in-version: 4.2
scope: render-export
tags: [user, essential]
prerequisites:
  - A project is open with an operator that has a Texture2D output selected or pinned in the Output Window (so "Render To File" can render it).
  - For the audio step, the project has a soundtrack on the timeline.
---

This checks the **Codec** picker in the [ui:RenderSettings|Render To File] window. A codec is the format your video is saved
in, and TiXL offers several: **H.264** and **HEVC** for everyday MP4 files, **ProRes** for editing, **VP9**
and **AV1** as modern web formats, **FFV1** for lossless (perfect-quality) masters, and the three **Hap**
options for fast playback in VJ and media-server tools. They all save with sound. You don't need to install
anything extra — every codec is built in. The way to check each one is a round-trip: render a few seconds,
then play the file back (either by loading it into a **[PlayVideo]** operator or opening it in a normal media
player) and confirm it looks right.

## Step: The Codec dropdown lists all codecs with friendly labels

**Action:**
Open the **Render To File** window and select the **Format & Quality** section in the left sidebar. With
**Render Mode** set to **Video**, find the **Codec** dropdown and open it.

**Expected:**
- A **Codec** dropdown is shown, with **H.264** selected by default.
- Opening it lists: **H.264**, **HEVC (H.265)**, **ProRes**, **VP9**, **AV1**, **FFV1**, **Hap**, **Hap
  Alpha**, **Hap Q** — note the friendly labels (e.g. "Hap Alpha", not "HapAlpha").

## Step: The filename ending follows the codec

**Action:**
Note the **Filename** field in the **Output Target** section (e.g. `render-v01.mp4`). Change **Codec** (in
**Format & Quality**) to **ProRes**, then to **FFV1**, then back to **H264**, watching the filename each time.

**Expected:**
- The ending of the filename follows the codec: ProRes → `.mov`, FFV1 → `.mkv`, VP9/AV1/H264 → `.mp4`.
- The name itself (e.g. `render-v01`) stays the same — only the ending changes.

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
- On a machine whose graphics card can speed up encoding, **H264** shows **"Hardware encoder (…)"** naming
  the graphics card it's using.
- On a machine that can't use the graphics card, **H264** shows **"Software encoder"** — TiXL encodes it
  on its own, no extra software needed.
- The non-H.264 codecs (ProRes, VP9, AV1, FFV1, Hap) show **"Software encoder"** too.
- Switching codecs swaps the line in place without shifting the rest of the panel.
- **No codec ever asks you to install anything or download a file** — they all work out of the box.

## Step: H.264 export produces a playable MP4 with audio

**Action:**
Set **Codec** to **H264**, choose a short range (a few seconds), make sure **Export Audio** is on, and press
**Render**. When it finishes, play the file back — load it into a **[PlayVideo]** operator or open it in a
normal media player.

**Expected:**
- A video file appears in the chosen folder.
- It plays back with the correct image and audible, in-sync sound.

## Step: HEVC export produces a playable MP4

**Action:**
Set **Codec** to **HEVC (H.265)**, choose a short range, press **Render**, then play it back with
**[PlayVideo]**.

**Expected:**
- A video file appears and plays back with the correct image. The indicator reads **"Hardware encoder
  (…)"** on a machine whose graphics card supports HEVC, otherwise **"Software encoder"** (in that case the
  render is noticeably slower).
- No prompts to install anything. (Note: a software-encoded HEVC file may not show a preview thumbnail in
  Windows Explorer or QuickTime, but it plays fine in **[PlayVideo]** and in VLC.)

## Step: ProRes export produces a playable MOV

**Action:**
Set **Codec** to **ProRes**, press **Render**, then play the result back with **[PlayVideo]**.

**Expected:**
- A video file appears and plays back with the correct image.
- The file is noticeably larger than the H.264 render of the same range.

## Step: VP9, AV1, and FFV1 each export and re-import

**Action:**
For each of **VP9**, **AV1**, and **FFV1** in turn: keep the range short, press **Render**, then
play the result back with **[PlayVideo]**. VP9 and AV1 take longer to render than H.264 — especially at
high resolution.

**Expected:**
- Each one produces a video file that plays back with the correct image.
- FFV1 looks identical to the original (lossless) and its file is much larger than the others.
- The **[PlayVideo]** operator shows no red error when loading any of them, and the editor never freezes during the render.

## Step: Hap exports with a size estimate

The Hap codecs are built in and need nothing extra installed.

**Action:**
Select **Hap** (also try **HapAlpha** / **HapQ**), keep the range short, **Render**, then play the result
back with **[PlayVideo]**.

**Expected:**
- The three Hap entries appear; the filename ends in `.mov`; the inline line reads **"Software encoder"** and
  **Render** is enabled — no prompts, nothing to install.
- A size estimate appears (e.g. **"Est. 1.9 GB"** with the dimensions) — Hap files are a predictable size, so
  the estimate is reliable; **HapAlpha** and **HapQ** are about twice the size of **Hap**. The estimate and the
  footer summary line agree.
- A video file appears and plays back with the correct image (**HapAlpha** keeps transparency; **HapQ** is
  higher quality and larger). With **Export Audio** on, it includes sound.

## Step: Codec choice survives save and reload

**Action:**
Set **Codec** to **AV1**, save the project, then close and reopen it (or reload). Open the **Render To File**
window again.

**Expected:**
- **Codec** is still **AV1**, and the **Filename** still ends in `.mp4`. (The choice was remembered.)

## Step: The content header has a section-aware help button

**Action:**
In the **Render To File** window, look at the top-right corner of the content panel (level with the section
title). Hover the **help (?)** icon on the **Source** section, then switch to **Format & Quality** and
**Output Target** and hover it again. Click it once.

**Expected:**
- A help icon sits in the header's top-right corner, vertically aligned with the title.
- Hovering shows a short formatted summary **specific to the active section** (Source vs Format & Quality vs
  Output Target) — the content changes as you switch sections.
- Clicking opens the documentation page (`help.tixl.app/using/ExportVideos`) in the default browser.

## Step: Parameters have inline (?) help

**Action:**
In the **Source** section, hover the small **(?)** icon next to **Range**, **Scale**, **Start**, **End**, and
**FPS**. Check **Format & Quality** (**Render Mode**, **Codec**, **Export Audio**) and **Output Target**
(**Filename**, **Auto-increment version**) too.

**Expected:**
- Each of those parameters shows a **(?)** marker that, on hover, explains the parameter in plain language.
- The segmented controls (Range, Scale, Render Mode) also carry a **(?)** after the control.
