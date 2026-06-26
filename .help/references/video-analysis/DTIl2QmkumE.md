---
video: DTIl2QmkumE
type: tutorial
date: 2022-07-04
title: Tooll 3 Tip#007 - Animating the Steps effect
duration: 0:03:23
focusesOn: [Steps]
---

A short scripted tip showing how the [Steps] image effect posterizes a gradient into bands, then animating it by wiring counters and an LFO into its offset, gradient rotation, and highlight color.

## Mentions
- 0:13→0:36 [Steps] [LinearGradient] · explained · scripted · Example · 88% — Quantizes an image's brightness into a set number of flat bands; feed it a smooth gradient to see the banding clearly and have a clean input to drive.
- 0:36→0:57 [Steps] · in-depth · scripted · Parameters · 85% — Count sets the number of bands, a repeat toggle tiles them, a distribution control biases the bands evenly or skewed to one side, and an offset/shift parameter slides them — the offset being the natural target for animation.
- 0:57→1:20 [Steps] · explained · scripted · Parameters · 82% — A single band can be picked out as a highlight color, and a faked shadow tint between bands cheaply suggests a 3D bevel from a flat 2D ramp.
- 1:26→1:55 [Steps] · explained · scripted · Example · 80% — Drive the band offset from an incrementing counter to make the bands march continuously in one direction; the counter's increment sets the scroll speed and its sign the direction.
- 1:58→2:24 [LinearGradient] [MirrorRepeat] · explained · scripted · Example · 72% — Animate a gradient's rotation by piping a stepped counter (e.g. 30° increments) into its angle, then wrap the result through a mirror-repeat so the rotating ramp tiles seamlessly instead of jumping.
- 2:29→2:47 [Steps] · explained · scripted · Example · 65% — Modulate the highlight color over time by sampling an oscillator-driven gradient (a saw shape gives a sharp ramp-and-reset pulse) into the highlight input.
- 3:02→3:09 [Glow] · passing · scripted · Tip · 60% — Adding a glow pass over the banded result makes the bright highlight band bloom for a richer look.
