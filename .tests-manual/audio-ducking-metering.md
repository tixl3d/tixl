---
id: audio-ducking-metering
title: Audio Graph — level metering and ducking
scope: audio
tags: [user, essential, hardware]
added: 2026-08-01
added-in-version: 4.3
prerequisites:
  - An empty project is open and audio output is audible.
  - A music file (.mp3 / .wav) is available in the project's assets.
---

Covers the metered-float path: tapping levels with [AudioLevel], the
[AudioBus] `Level` output, and driving a duck with [DuckAudioLevel].

## Step: Bus level output meters the mix

**Action:**
Build a small graph: [AudioToneGenerator] → [AudioBus] (evaluated via the
render chain or pinning). Wire the bus's `Level` output through a
[PlotValueCurve] (or [FloatToString]) so it's visible. Trigger the tone.

**Expected:**
- The plotted level rises while the tone sounds and falls back to zero after.
- The curve is steady and continuous — no erratic drops to zero while audio
  is clearly sounding.
- The bus's own `Volume` does *not* change the metered level (it meters
  before the master volume).

## Step: AudioLevel taps a source inline

**Action:**
Insert an [AudioLevel] between the tone generator and the bus. Wire its
`Level` output to a plot.

**Expected:**
- Audio still plays unchanged through the tap.
- The plotted level follows the tone's envelope, scaled by the tone's volume.

## Step: Ducking music under a tone

**Action:**
Add an [AudioClip] with a music file (AutoPlay on) and wire its
`AudioReference` through its own [CombineAudio] into the bus, so both music
and tone play. Wire the [AudioLevel] tap's `Level` (from the tone) into a
[DuckAudioLevel], and its `Gain` output into the music's [CombineAudio]
`Volume`. Trigger the tone repeatedly.

**Expected:**
- Each time the tone sounds, the music dips smoothly and recovers after the
  tone stops.
- `Attack` sets how fast it dips, `Release` how fast it recovers, `Amount`
  how deep, and `Threshold` how loud the tone must be before ducking starts.
- The `GainAndBias` curve shapes how aggressively level maps to duck depth
  ((0.5, 0.5) behaves neutral).
