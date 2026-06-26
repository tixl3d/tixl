---
video: J2Rqx3szSus
type: tutorial
date: 2022-07-19
title: Tooll 3 Tip#015 - Draw Quads with Variations
duration: 0:04:22
---

A short tip showing how the W (fourth) point attribute can drive per-quad variation, and how [SelectPoints] writes a spatial selection back into W to mask large point clouds interactively.

## Mentions
- 0:16→0:59 [GridPoints] · explained · scripted · Example · 88% — Generate a volumetric point cloud (e.g. 10×10×10) as the source for instanced quads; the per-point W value starts uniform and is what you later vary.
- 0:21→0:30 [RenderTarget] · passing · scripted · Tip · 70% — Pipe a point-quad draw through it to get multisampled anti-aliasing on the result.
- 0:59→1:11 [GridPoints] · explained · scripted · Parameters · 82% — Set the initial W attribute and tweak grid depth, count, point size and overall scale to control how the cloud reads before any variation is applied.
- 1:11→1:18 [RandomizePoints] · explained · scripted · Example · 80% — Randomize the W attribute across a cloud so a downstream quad draw renders each instance with its own size/color/rotation.
- 1:33→2:06 · explained · scripted · Parameters · 75% — A varying-quad draw maps each point's W to scale, rotation, a sprite texture, orientation on/off, and depth-write on/off (disable depth-write for semi-transparent overdraw). (Operator name not in vocabulary — see UNSURE.)
- 2:06→2:33 [ui:GradientEditor] · explained · scripted · Tip · 80% — Shade instances across a distribution by editing a color gradient and a value curve keyed to W; Alt-click adds keyframes to reshape the mapping.
- 2:36→2:48 [SelectPoints] · in-depth · scripted · Concept · 88% — Writes a 0..1 selection state into each point's W attribute based on a region, so a single operator can mask which instances react downstream.
- 2:48→3:14 [SelectPoints] [ui:Gizmo] · in-depth · scripted · Example · 85% — Enable its gizmo to drag the selection region through the cloud live; scale, stretch and rotate the region to reshape what gets selected.
- 3:14→3:30 [SelectPoints] · explained · scripted · Parameters · 80% — Falloff softens the selection edge and the region shape can switch between sphere, box and a noise field for organic masks.
- 3:30→3:41 [SelectPoints] [Time] · explained · scripted · Example · 78% — Drive the noise-shape phase from a time source so the selection field animates on its own.
- 3:41→3:49 [SelectPoints] · explained · scripted · Tip · 78% — Chain multiple selections, multiplying or adding their W contributions to compose complex masks.
