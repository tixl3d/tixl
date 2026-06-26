---
video: Jpvyg-LR3f0
type: tutorial
date: 2022-06-29
title: Tooll 3 Tip#002 - Raster Effects
duration: 0:05:58
focusesOn: [Raster], [ValueRaster]
---

A short tip walking through the versatile Raster operator: its presets and knobs, then driving it from an input texture and stacking offset copies into a CMYK-style halftone print effect.

## Mentions
- 0:12→0:35 [Raster] · explained · scripted · Concept · 90% — Ships with a library of presets you can browse, and holding Alt while switching lets you interpolate between two presets for in-between looks.
- 0:36→0:56 [ValueRaster] · passing · scripted · Concept · 60% — The float-list counterpart that outputs raster values for driving other operators rather than rendering pixels — useful when you want the same line/dot pattern as data instead of as an image; not shown directly here, but the pattern controls demonstrated transfer to it.
- 0:39→0:56 [Raster] · in-depth · scripted · Parameters · 88% — Line-width-ratio places lines at or between grid intersections while dot-size controls whether you get crosses or plain separator lines; zero either the line width or line ratio to drop the lines and keep only dots.
- 1:15→1:36 [Raster] · explained · scripted · Performance · 85% — Being a fragment shader, the cost is identical whether it draws many tiny dots or one large one, so density is free to tune.
- 1:36→2:05 [Raster] · explained · scripted · Parameters · 84% — Drag with the left mouse to set opacity for transparent-to-opaque output, then raise the feather value to soften the dots into a blurred grain.
- 2:12→2:56 [Raster] [LoadImage] · in-depth · experiment · Example · 82% — Feed an image into the raster's input so the pattern samples the picture's brightness; bump the background-color brightness to kill the dark fringe that appears over the source.
- 3:08→3:48 [Raster] · explained · experiment · Tip · 75% — Run it fully transparent to punch see-through holes in the pattern, then animate the hole/pin parameter for a shifting perforation effect.
- 3:48→5:06 [Raster] [Layer2d] · in-depth · experiment · Example · 78% — Build a CMYK halftone: stack several offset copies of the same rasterized image as layers set to additive blending, each tinted a process color (cyan/yellow/etc.), since layering blends far easier than mixing images directly.
