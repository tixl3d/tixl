---
id: beat-lock-phase-model
title: Beat Lock with Phase Model
scope: project-settings
tags: [user, audio, live]
added: 2026-09-11
added-in-version: 4.3
prerequisites:
  - A project is open.
  - An audio input device is available that carries music with a clear beat (a loopback / "what you hear" device playing a track at a steady tempo, e.g. 128 BPM, works best).
  - The folder `dance-phase` next to the editor executable contains `bar-phase.onnx` and `bar-phase.meta.json`.
related-help:
  - ../.help/embedded/ProjectSettings_Timing.md
  - ../.help/docs/using/LivePerformances.md
---

The **Phase Model** beat lock source uses a neural network to find the tempo and
the start of each bar in live audio, without any tapping. This set checks that the
model loads, that playback locks onto the music, and that switching back to onset
detection still works.

## Step: Selecting the phase model source

**Action:**
Open the Project Settings window, switch **Audio Source** to **External Device**, pick the
input device that carries the music, and set **Sync Mode** to **Tapping**. Make sure
**Enable audio beat lock** is checked.

**Expected:**
- A **Beat Lock Source** segmented button appears below the checkbox with the options **Onset Detection** and **Phase Model**.
- Selecting **Phase Model** shows a hint line below the button that reads `Loading bar-phase model...` or `Waiting for audio...` at first.

## Step: Locking onto the music

**Action:**
Start the music on the selected input device and watch the hint line for a few seconds.

**Expected:**
- Within about 5 seconds the hint changes to `Tracking: <bpm> BPM, phase error ±<n> bars`.
- The reported BPM is within 2 BPM of the track's tempo after about 10 seconds.
- The phase error settles below 0.15 bars on a track with a clear beat.
- The BPM readout in the timeline toolbar follows the same value.

## Step: Verifying the bar phase visually

**Action:**
In the graph, add an `[AnimValue]` with a `Ramp` shape and a rate of 1 bar, and
connect it to a visible parameter such as the scale of a `[RadialGradient]`. Play the
project.

**Expected:**
- The ramp restarts on every bar start of the music, aligned with the downbeat, without any tap or resync.
- Tapping the sync keys (`Z` / `X`) is not required and does not break the lock.

## Step: Tuning the offset

**Action:**
Change **Beat Sync Offset (sec)** to `0.1`, then back to `0`.

**Expected:**
- The ramp restart shifts later by about a tenth of a second and back.
- The BPM readout stays unchanged.

## Step: Switching back to onset detection

**Action:**
Switch **Beat Lock Source** to **Onset Detection**.

**Expected:**
- The hint line disappears.
- The classic behaviour returns: the lock only engages after tapping a resync with `X` on a bar start.

## Step: Missing model files

**Action:**
Close the editor, rename the folder `dance-phase` next to the executable to `dance-phase.off`,
start the editor, and select **Phase Model** again.

**Expected:**
- The hint reads `Bar-phase model not found in <path>`.
- Playback keeps running at the project's BPM; nothing crashes.
- Renaming the folder back and restarting the editor restores tracking.
