---
id: continuous-capture
title: Render Export — Continuous Capture
added: 2026-06-21
added-in-version: 4.3
scope: render-export
tags: [user, essential]
prerequisites:
  - A project is open with an operator that has a Texture2D output selected or pinned in the Output Window (so "Render To File" can render it).
  - Ideally one with visible motion, so a recording's frames are easy to tell apart.
related-help:
  - ../.help/docs/using/ExportVideos.md
---

Most exports render a fixed range with a known start and end. **Continuous** is different: it's an
open-ended recording that starts right away and keeps going until you stop it — useful for capturing
a live performance or a session of unknown length. You pick one of two timing modes under **Source**:
**Realtime** records the live output exactly as you perform it (video only, no audio yet);
**Deterministic** steps through time at the chosen frame rate so every frame is captured even if your
machine can't keep up live. While a continuous recording runs, the usual progress bar is replaced by a
sweeping "still going" indicator.

## Step: Continuous appears as a fourth Range mode

**Action:**
Open the [ui:RenderSettings|Render To File] window and select the **Source** section in the left sidebar. Look at the
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
- The **Frame Rate** control is disabled and reads **Fixed FPS**, with a hint that a variable frame rate is coming
  soon.
- With **Realtime** selected, the **Resolution Scale** row is disabled with a hint that realtime capture uses the
  output's own resolution.

## Step: Slower codecs show a warning, but recording is still allowed

**Action:**
With **Continuous** selected, go to the **Format & Quality** section and set **Codec** to a slower one
(e.g. **VP9** or **ProRes**). Then set it back to **H264** on a machine whose graphics card can speed up encoding.

**Expected:**
- For a codec that can't use the graphics card, a warning line appears under the codec indicator: roughly "No hardware
  encoder — continuous capture may drop or duplicate frames."
- The **Render** button stays enabled — it warns you but doesn't stop you.
- With **H264** on a machine that can use the graphics card, the warning is absent.

## Step: Realtime capture grabs the live output until stopped

**Action:**
Set **Range** to **Continuous**, **Clock** to **Realtime**, **Codec** to **H264**. Start playback so the
output is animating, then press **Render** (or the render-animation keyboard shortcut). Let it run a few seconds while
the output keeps playing, then press the capture control again to stop.

**Expected:**
- Recording starts immediately; the footer replaces the progress bar with a sweeping activity segment and
  reads "Capturing… N frames / <elapsed>", the count rising over time.
- A thin sweeping indicator also appears along the top edge of the [ui:OutputWindow|Output Window].
- Playback keeps running live and responsive during the capture (it is **not** forced or scrubbed).
- Pressing capture again stops and saves the file; the status reads "Captured N frames (…) to …", and the
  saved video plays back showing the live motion that was on screen. (No audio in this mode.)

## Step: Deterministic capture advances time until stopped

**Action:**
Set **Clock** to **Deterministic**, press **Render**, let it run briefly, then stop with the capture control.

**Expected:**
- The same activity indicator and "Capturing…" readout appear.
- Time advances steadily at the target frame rate (the output steps forward frame by frame, even if it
  can't keep up live).
- Stopping saves a video that plays back. With **Export Audio** on, it includes the soundtrack.

## Step: Continuous choice survives save and reload

**Action:**
With **Range** set to **Continuous** and **Clock** set to **Deterministic**, save the project, then close and
reopen it (or reload). Reopen the **Render To File** window's **Source** section.

**Expected:**
- **Range** is still **Continuous** and **Clock** is still **Deterministic**.
