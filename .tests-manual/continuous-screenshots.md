---
id: continuous-screenshots
title: Output — Continuous screenshots
added: 2026-06-16
added-in-version: 4.3
scope: output
tags: [output, essential]
prerequisites:
  - A project is open with an output that resolves to a Texture2D (e.g. any image operator pinned in the Output window).
related-help:
  - ../.help/docs/using/ExportVideos.md
---

Verifies the continuous screenshot mode on the Output window's screenshot icon: Ctrl-clicking it
starts saving a screenshot on a fixed interval, the icon pulses in the attention colour in sync
with each capture, and a plain click stops it. The interval is configured in user settings.

## Step: Single screenshot still works

**Action:**
In the Output window toolbar, plain-click the screenshot (camera) icon.

**Expected:**
- A single `.png` lands in `<project>/Screenshots/` with a timestamped name.
- The icon does not change appearance; no pulsing starts.
- Hover shows the tooltip "Save screenshot" with a note "Ctrl+click to capture continuously."

## Step: Ctrl-click starts continuous mode

**Action:**
Hold `Ctrl` and click the screenshot icon.

**Expected:**
- The icon turns to the magenta **attention** colour and starts **pulsing** — full opacity right after each capture, fading toward ~50% just before the next one.
- A new screenshot is written immediately, then again every interval (default **5 s**) into `<project>/Screenshots/`.
- Hover shows "Stop continuous screenshots" with the current interval (e.g. "Saving a screenshot every 5s. Click to stop.").

## Step: Plain click stops continuous mode

**Action:**
Plain-click the (pulsing) screenshot icon.

**Expected:**
- The pulsing stops; the icon returns to its normal emphasized look.
- No further screenshots are written.

## Step: Interval is configurable

**Action:**
Open **Settings → Interface → Output** and set "Continuous screenshot interval" to e.g. 1 s. Ctrl-click the screenshot icon again and watch the `Screenshots/` folder.

**Expected:**
- Screenshots are now written about once per second, and the icon's pulse period matches the new interval.
- Stop again with a plain click.

## Step: Right-click context menu — interval presets

**Action:**
Right-click the screenshot icon.

**Expected:**
- A popup shows a "Start/Stop continuous screenshots" item, a **"Capture every"** group with presets (1 second, 5 seconds, 10 seconds, 30 seconds, 1 minute, 5 minutes, 10 minutes), and a **"File format"** group (PNG / JPG).
- The preset matching the current interval is check-marked. Picking another updates the interval (the Settings slider and the pulse period both reflect the new value, and the choice survives a restart).

## Step: Right-click context menu — file format

**Action:**
In the context menu, switch the file format to **JPG**, then take a single screenshot and start a short continuous run.

**Expected:**
- The check-mark moves to JPG.
- New files land in `Screenshots/` with a **`.jpg`** extension and are noticeably smaller than the PNGs.
- Switching back to PNG restores `.png` output. The choice persists across restarts.

## Step: Continuous mode pauses during a video export

**Action:**
Start continuous mode (Ctrl-click). While it is pulsing, start a video/image-sequence export (Render Animation icon). When the export finishes, observe the screenshot icon.

**Expected:**
- While exporting, no continuous screenshots are written to `Screenshots/` (the export owns the capture queue).
- After the export completes, continuous capture resumes automatically and the icon keeps pulsing.
