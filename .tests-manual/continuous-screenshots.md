---
id: continuous-screenshots
title: Output — Continuous screenshots
added: 2026-06-16
added-in-version: 4.3
scope: output
tags: [user, output, essential]
prerequisites:
  - A project is open with an output that resolves to a Texture2D (e.g. any image operator pinned in the Output window).
related-help:
  - ../.help/docs/using/ExportVideos.md
---

The [ui:OutputWindow|Output window] can keep saving screenshots on its own — handy for capturing a long-running
piece without sitting at the keyboard. This checks that Ctrl-clicking the screenshot icon starts
saving an image at a fixed interval, that the icon pulses with each capture so you can see it's
working, and that a plain click stops it. You can change how often it captures in Settings.

## Step: Single screenshot still works

**Action:**
In the Output window toolbar, plain-click the screenshot (camera) icon.

**Expected:**
- A single image appears in the project's **Screenshots** folder, named with the date and time.
- The icon does not change appearance; no pulsing starts.
- Hover shows the tooltip "Save screenshot" with a note "Ctrl+click to capture continuously."

## Step: Ctrl-click starts continuous mode

**Action:**
Hold `Ctrl` and click the screenshot icon.

**Expected:**
- The icon turns the magenta **attention** colour and starts **pulsing** — bright right after each capture, fading toward dim just before the next one.
- A new screenshot appears immediately, then another every interval (default **5 s**) in the project's **Screenshots** folder.
- Hover shows "Stop continuous screenshots" with the current interval (e.g. "Saving a screenshot every 5s. Click to stop.").

## Step: Plain click stops continuous mode

**Action:**
Plain-click the (pulsing) screenshot icon.

**Expected:**
- The pulsing stops; the icon returns to its normal emphasized look.
- No further screenshots are written.

## Step: Interval is configurable

**Action:**
Open **Settings → Interface → Output** and set "Continuous screenshot interval" to e.g. 1 s. Ctrl-click the screenshot icon again and watch the **Screenshots** folder.

**Expected:**
- Screenshots now appear about once per second, and the icon's pulse rate matches the new interval.
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
- New screenshots appear in the **Screenshots** folder as **JPG** files and are noticeably smaller than the PNG ones.
- Switching back to PNG restores PNG screenshots. The choice persists across restarts.

## Step: Continuous mode pauses during a video export

**Action:**
Start continuous mode (Ctrl-click). While it is pulsing, start a video/image-sequence export (Render Animation icon). When the export finishes, observe the screenshot icon.

**Expected:**
- While exporting, no continuous screenshots appear in the **Screenshots** folder (the export takes over capturing).
- After the export completes, continuous capture resumes automatically and the icon keeps pulsing.
