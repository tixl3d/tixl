---
video: -cHz2vL5ETA
type: tutorial
date: 2024-09-30
title: Copy Cat Tutorials — Rebuilding Glowing Ribbons with 2D Effects
duration: 0:58:24
---

A "copycat" walkthrough recreating a glowing, squeezed-ribbon animation found on Reddit, built almost entirely from 2D line/point operators plus a post-processing chain (blur, glow, color remap). The transcript degrades into repeated ASR noise after roughly 36:40; all real content is in the first ~37 minutes.

## Mentions
- 0:43→0:50 [CompareImages] · explained · scripted · Tip · 80% — Use it as a reference overlay: it slices through two images so you can eyeball how your build matches a target's color distribution side by side.
- 1:23→2:28 [PlayVideo] · passing · scripted · Example · 75% — Load a downloaded MP4 as a moving reference clip to scrub and study frame by frame while rebuilding it.
- 5:54→6:10 [LinePoints] [RepeatAtPoints] · explained · scripted · Example · 80% — Wire [LinePoints] into [RepeatAtPoints] to clone one line across a set of positions; on its own the copies stack invisibly until you give them spread and orientation.
- 7:04→7:30 [RepeatAtPoints] · explained · scripted · Parameters · 80% — The orientation axis decides which way the copies fan out; switch it (e.g. to Y) and raise the count so stacked duplicates spread into a usable grid of lines.
- 8:25→9:11 [TransformPoints] [SelectPoints] · explained · scripted · Example · 80% — Pair a point selection with a transform so a squeeze only affects points inside the selected volume, leaving the rest untouched.
- 8:53→9:04 [DrawLines] · passing · scripted · Parameters · 65% — Disable "use selection for width" so a selection drives the deformation but not the rendered line thickness.
- 9:22→11:01 [SelectPoints] · explained · scripted · Parameters · 80% — Switch the volume shape from sphere to plane and rotate it to get a directional falloff band; the bias softens the selection edge for a smoother transition.
- 11:56→12:34 [DrawRibbons] · explained · scripted · Gotcha · 80% — It needs correctly oriented points: if ribbons come out twisted, rotate the source points' orientation (about 90°) so the flat side faces the camera.
- 12:41→13:14 [SetMaterial] · explained · scripted · Example · 75% — Set the base color to a gradient and rotate it 90° to shade ribbons across their width instead of along their length.
- 13:44→14:48 [SetPointAttributes] · explained · scripted · Concept · 70% — Spreads a gradient's colors across all points in a buffer so a color ramp maps onto a line set as a per-point attribute.
- 15:07→15:24 [SetPointAttributes] · explained · scripted · Parameters · 75% — The repeat mapping mode tiles the gradient across the points, so a narrow range becomes a repeating banded pattern you can scroll.
- 15:47→16:19 [SetPointAttributes] · passing · scripted · Tip · 65% — Animate the gradient phase by linking it to time with a slow preset, then enable background motion to preview the looping crawl.
- 16:49→17:20 [AnimValue] · passing · scripted · Tip · 65% — Drive a phase value from an oscillator instead of raw time so you can shape (e.g. to a sine) and slow the repeating pattern's movement.
- 18:16→21:05 [BlendWithMask] [LinearGradient] · in-depth · scripted · Example · 85% — Feed a [LinearGradient] as the mask to make blur fall off across the frame; align the gradient angle (here ~131°) and use ping-pong to blur both edges while keeping the center sharp.
- 19:35→20:11 [ImageLevels] · explained · scripted · Tip · 75% — Drop it into a chain to read the average brightness/histogram of the current image, useful for judging how close to HDR your output is.
- 19:43→21:05 [LinearGradient] · in-depth · scripted · Parameters · 80% — Linear interpolation gives a hard, "nerdy" precise ramp; switching to smooth interpolation eases the gradient and makes effects driven by it (like a blur mask) ramp in far more gracefully.
- 21:07→22:51 [Glow] · in-depth · scripted · Gotcha · 85% — It barely glows until the source is actually HDR: push a color's brightness above 1 (Ctrl-drag the color past white) and the glow then spreads that over-bright energy across the image.
- 23:01→23:35 [SetPointAttributes] · explained · scripted · Tip · 70% — Smooth interpolation plus an asymmetric range gives a directional ramp — a sharp edge on one side, soft on the other — instead of a symmetric band.
- 24:09→25:35 [Layer2d] [RoundedRect] · explained · scripted · Example · 75% — Composite a soft glowing shape over a scene by scaling a [Layer2d] down and feeding it a [RoundedRect]; crank feathering and tune the aspect ratio for the shape you need.
- 26:12→27:10 [AnimValue] · explained · scripted · Parameters · 75% — Zero out unwanted axes of the amplitude so an oscillation moves cleanly along one direction, and set the shape to sine for a slow, gradual back-and-forth.
- 27:40→28:13 [SampleGradient] · passing · scripted · Tip · 60% — Sample a gradient from a moving value to tie an element's brightness to its position — dark at the extremes, bright in the middle — though it expects a scalar, not a vec2.
- 28:57→30:00 [RemapColor] · in-depth · scripted · Concept · 85% — Maps an image's tonal range onto a gradient (black→left, white→right) and clamps at 1, so it fits LDR/after-glow stages but not HDR; Alt-pick colors straight from a reference image to match its palette.
- 32:55→33:18 [ResampleLinePoints] · explained · scripted · Gotcha · 75% — Smooths a line by re-distributing points along it, but it doesn't handle multiple disconnected line segments — they get mangled, so it's unsuitable for a repeated multi-line set.
- 34:24→35:10 [ChromaticAbberation] · passing · scripted · Tip · 65% — Add it for a subtle lens fringe; the presets are the quick way to find a distortion look before fine-tuning size.
