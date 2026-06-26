---
video: ElW_0uaeiNA
type: tutorial
date: 2022-07-15
title: Tooll 3 Tip#012 - Noisy Point Trails
duration: 0:02:19
focusesOn: [PointTrail], [PointTrailFast], [SimNoiseOffset]
---

A short tip showing how to feed animated, noise-displaced points into a cyclic trail buffer to draw streaking trails, then layer extra noise and fog on the result.

## Mentions
- 0:06→0:26 [GridPoints] [DrawPoints] · passing · scripted · Example · 70% — Starting a point setup by laying out a base set and rendering it, then displacing it with animated noise before any trailing — the source positions a trail buffer later records.
- 0:13→0:26 [AddNoise] · explained · scripted · Example · 78% — Driving displacement with a time-animated noise so the source points keep moving frame to frame, which is what makes a recorded trail trace a visible path rather than a static smear.
- 0:26→1:05 [PointTrail] · in-depth · scripted · Concept · 88% — Records each frame's point positions into a cyclic GPU buffer roughly 100× the input size, so every point leaves a trail of its recent history; because it lives on the GPU the history can run very long and effectively forever.
- 0:26→1:05 [PointTrailFast] · passing · scripted · Comparison · 55% — The lighter-weight sibling for the same record-positions-into-a-history-buffer trick; reach for it when the full trailing operator's per-point overhead is more than you need for long buffers — not directly demonstrated here, where the standard variant is used.
- 1:05→1:55 [PointTrail] [AddNoise] · explained · scripted · Tip · 80% — The trail output is itself a point buffer, so you can keep processing it — e.g. apply a second, simulation-driven noise on top of the trailed points to make the whole trail wobble and evolve beyond the original displacement.
- 1:25→1:55 [SimNoiseOffset] · explained · scripted · Tip · 60% — Swapping a static noise displacement for a simulation-driven offset that accumulates frame to frame, so the displacement keeps drifting instead of holding a fixed shape — layer it on top of already-trailed points to make the whole history evolve over time. The maintainer's curated links flag this as the operator behind the "noise with a simulation" step, though it isn't named on screen.
- 1:55→2:08 [SetFog] · passing · scripted · Tip · 65% — Adding atmospheric fog over the trailed points to fade distant ones and give the streaks more depth and polish.
