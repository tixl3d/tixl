---
id: continuous-capture
title: Render Export — Continuous Capture
added: 2026-06-21
added-in-version: 4.3
scope: render-export
tags: [essential]
prerequisites:
  - A project is open with an operator that has a Texture2D output selected or pinned in the Output Window (so "Render To File" can render it).
  - Ideally one with visible motion, so a recording's frames are easy to tell apart.
related-help:
  - ../.help/docs/using/ExportVideos.md
---

Covers the **Continuous** range mode in the "Render To File" window: an open-ended recording that starts
immediately and runs until the tester stops it, instead of rendering a fixed start–end range. There are two
clock models (set under the **Source** section): **Realtime** grabs the live output as you perform (video
only, no audio yet); **Deterministic** advances time at the target FPS until stopped. The progress bar is
replaced by a sweeping activity indicator while a continuous capture runs.

## Step: Continuous appears as a fourth Range mode

**Action:**
Open the **Render To File** window and select the **Source** section in the left sidebar. Look at the
**Range** segmented button.

**Expected:**
- The **Range** control offers **Custom**, **Loop**, **Soundtrack**, and **Continuous**.
- Selecting **Continuous** hides the **Scale / Start / End** rows and instead shows a **Clock** control
  (**Realtime** / **Deterministic**) and a **Frame Rate** control.

## Step: The continuous options read correctly

**Action:**
With **Continuous** selected, switch **Clock** between **Realtime** and **Deterministic**, and inspect the
**Frame Rate** control.

**Expected:**
- A short hint under **Clock** changes: Realtime mentions grabbing the live output and "video only / no audio
  yet"; Deterministic mentions advancing at the target FPS, frame-perfect but not realtime.
- The **Frame Rate** control is disabled and reads **Fixed FPS**, with a hint that Variable (VFR) is coming
  soon.
- With **Realtime** selected, the **Resolution Scale** row is disabled with a hint that realtime capture uses the
  native output resolution.

## Step: Software codecs show a warning, but capture is still allowed

**Action:**
With **Continuous** selected, go to the **Format & Quality** section and set **Codec** to a software-only
codec (e.g. **VP9** or **ProRes**). Then set it back to **H264** on a machine with a hardware encoder.

**Expected:**
- For a codec with no hardware encoder, a warning line appears under the codec indicator: roughly "No hardware
  encoder — continuous capture may drop or duplicate frames."
- The **Render** button stays enabled (warn, don't block).
- With **H264** on a hardware-encoder machine, the warning is absent.

## Step: Realtime capture grabs the live output until stopped

**Action:**
Set **Range** to **Continuous**, **Clock** to **Realtime**, **Codec** to **H264**. Start playback so the
output is animating, then press **Render** (or the render-animation shortcut). Let it run a few seconds while
the output keeps playing, then press the capture control again to stop.

**Expected:**
- Recording starts immediately; the footer replaces the progress bar with a sweeping activity segment and
  reads "Capturing… N frames / <elapsed>", the count rising over time.
- A thin sweeping indicator also appears along the top edge of the Output Window.
- Playback keeps running live and responsive during the capture (it is **not** forced or scrubbed).
- Pressing capture again stops and finalizes the file; the status reads "Captured N frames (…) to …", and the
  written `.mp4` plays back showing the live motion that was on screen. (No audio in this mode.)

## Step: Deterministic capture advances time until stopped

**Action:**
Set **Clock** to **Deterministic**, press **Render**, let it run briefly, then stop with the capture control.

**Expected:**
- The same activity indicator and "Capturing…" readout appear.
- Time advances steadily at the target FPS (the output steps forward frame by frame, even if encoding is
  slower than realtime).
- Stopping finalizes a playable file. With **Export Audio** on, it carries the soundtrack.

## Step: Continuous choice survives save and reload

**Action:**
With **Range** set to **Continuous** and **Clock** set to **Deterministic**, save the project, then close and
reopen it (or reload). Reopen the **Render To File** window's **Source** section.

**Expected:**
- **Range** is still **Continuous** and **Clock** is still **Deterministic**.
