---
video: DJKb0wDr0Zs
type: tutorial
date: 2022-07-11
title: "Tooll 3 Tip#008 - Procedural Camera Animations"
duration: 0:03:59
focusesOn: [OrbitCamera]
---

A short tip walking through the parameters of the RandomCamera operator for procedural camera moves, plus how to drive its seed and shake inputs from a beat counter and noise.

## Mentions
- 0:09→0:17 [DrawCamGizmos] · passing · scripted · Example · 70% — Drop it into a scene to visualize the camera's position and frustum while you tune an animated camera rig.
- 0:21→0:42 · passing · scripted · Tip · 80% — Hold Alt over a parameter set to flip through built-in presets ("dance with me", "mosquito", "drunken") as starting points before fine-tuning.
- 0:42→2:32 [OrbitCamera] · in-depth · scripted · Parameters · 80% — A full pass over the orbiting-camera knobs: center and orbit distance set the framing, spin rate/offset and orbit/aim angles drive the move, and a per-parameter wobble (complexity + speed) layers continuous oscillation on any of them — drive spin offset or seed from a beat for cuts and forward jumps. (Spoken as "RandomCamera"; this is the dedicated tutorial for it.)
- 0:42→1:18 · explained · scripted · Parameters · 75% — Orbit-style camera knobs: center point, orbit distance, spin rate, and spin offset, where spin offset is the natural input to nudge forward on a beat.
- 1:18→1:52 · explained · scripted · Parameters · 70% — Orbit angle sets elevation above the horizon (90° looks straight down, negatives look up), while aim pitch/angle rotate the look direction about the camera position itself.
- 1:52→2:32 · explained · scripted · Parameters · 75% — A per-parameter wobble (complexity + speed) adds continuous oscillation around each value; zero complexity disables it, so raise it to make e.g. roll sway by a few degrees.
- 2:32→2:52 [CountInt] · explained · scripted · Example · 60% — Feed an incrementing integer counter into a camera's random seed so each step jumps to a fresh, repeatable framing — handy for cutting on every beat.
- 2:56→3:23 [TriggerAnim] [PerlinNoise] · explained · scripted · Example · 60% — Wire a [TriggerAnim] into a rotation/shake offset to scale animated noise on each trigger, producing decaying camera shake whose intensity and speed you dial back to taste.
- 3:23→3:33 · passing · scripted · Parameters · 65% — Standard camera tail parameters: aspect ratio (0 takes the scene default), far clipping plane, and field of view.
