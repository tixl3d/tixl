---
video: o-Ojg_LOi4k
type: tutorial
date: 2022-07-18
title: Tooll 3 Tip#014 - Complexity with the Invert Blend Mode
duration: 0:01:48
---

A short tip showing how the Invert blend mode turns a plain field of overlapping quads into rich, animated abstract patterns, layering point generators, transforms, and an LFO to drive the complexity.

## Mentions
- 0:09→0:17 [RenderTarget] · passing · scripted · Example · 70% — Collect overlapping draw calls into one buffer so a blend mode can combine them; here it's where Invert blending accumulates the result.
- 0:17→0:31 [RenderTarget] · explained · scripted · Tip · 80% — Switching the buffer's blend mode to Invert makes each overlapping shape flip what's beneath it, generating intricate moiré-like interference from a trivial setup.
- 0:31→0:44 · passing · scripted · Example · 60% — Driving a parameter with a slowed-down LFO set to a wave shape adds gentle animation to an otherwise static pattern.
- 0:44→1:02 [TransformPoints] · explained · scripted · Tip · 80% — Translating in point space rather than object space, then rotating, multiplies the visual complexity of an overlapping-shape pattern far beyond what an object-space move gives.
- 1:02→1:20 · explained · scripted · Example · 65% — Connecting a time value (raising its multiplier, e.g. ~15) into a transform's rotation continuously evolves the interference pattern over time.
- 1:20→1:34 [GridPoints] · passing · scripted · Example · 75% — Swapping a scatter source for a single-depth-layer grid of points gives the same Invert-blend trick a more ordered, lattice-like character.
