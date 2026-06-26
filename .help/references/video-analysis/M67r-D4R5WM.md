---
video: M67r-D4R5WM
type: tutorial
date: 2022-07-17
title: Tooll 3 Tip#013 - Drive Animations with Audio Reactions
duration: 0:04:31
focusesOn: [AudioReaction]
---

A short tip showing how to feed external audio into the graph and use [AudioReaction] to drive a particle scene — clearing the background and re-seeding randomness on each beat, and steering camera and noise with the level output.

## Mentions
- 0:12→0:38 [ui:AudioInput] · explained · scripted · Tip · 80% — Before any reaction works you must open the time settings, choose an audio input channel, then re-scan the sound-input devices so the OS sources (microphone, browser loopback) actually appear in the list.
- 0:38→0:51 [AudioReaction] · explained · scripted · Example · 90% — Drop it in to get a live FFT spectrum that responds to whatever input channel is active; this is the source you wire animation parameters to.
- 0:51→1:16 [AudioReaction] · in-depth · scripted · Parameters · 88% — Its input modes let you pick between raw vs. normalized FFT, and frequency bands that are live, decaying, or attack-following; the attack-following mode is the default because it cleanly isolates beats.
- 1:16→1:47 [AudioReaction] · in-depth · scripted · Parameters · 85% — A movable frequency window selects which band counts as a "beat" — slide it left for bass, right for hi-hats — so you tune which part of the track triggers the reaction.
- 2:05→2:18 [ui:Graph] · passing · scripted · Example · 55% — Walks a prepared scene's signal chain — scattered points with noise, drawn meshes, a moving camera, fog, then glow — as the targets the audio will drive.
- 2:45→2:54 [AudioReaction] · explained · scripted · Example · 80% — Route its continuous level output into a gradient/color value, and trigger a background clear on each detected beat for a strobing backdrop.
- 2:55→3:15 [AudioReaction] · explained · scripted · Example · 82% — Feed the integer hit-count output into a [Random] seed so every beat snaps to a fresh value — here jumping the camera to a new perspective.
- 3:15→3:53 [AudioReaction] [TriggerAnim] · explained · scripted · Example · 70% — Use the per-beat "was hit" trigger to fire a [TriggerAnim], whose shake mode and frequency turn each impulse into a decaying animated jolt on a target parameter.
