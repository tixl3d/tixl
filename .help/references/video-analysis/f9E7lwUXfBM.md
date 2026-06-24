---
video: f9E7lwUXfBM
type: tutorial
date: 2022-06-30
title: Resolution Magic — how output size, image inputs, and the magic resolution parameter interact
duration: 6:37
---

A scripted single-narrator "tip of the day" walking through how resolution flows through TiXL's image pipeline. It shows that resolution can be driven from the right (the output window), from the left (an incoming image/texture), or pinned explicitly via the magic `0,0` and `-1` values of a resolution parameter. It closes with how radius-style parameters use image height as their unit, ignoring width.

## Mentions
- 0:25→0:31 [Blob] · passing · scripted · 90% — Spinning up a [Blob] as the example image effect to experiment with resolution.
- 0:31→0:48 [ui:OutputWindow] · explained · scripted · 90% — How the [ui:OutputWindow] in "fill" mode sets the rendered size and what shrinking it does to a texture.
- 0:48→1:02 · explained · scripted · 70% — Why TiXL keeps aspect ratio so a circle stays a circle regardless of requested output size.
- 1:02→1:18 · explained · scripted · 60% — Overriding the automatic size to a predictable resolution like Full HD or 4K from the output window.
- 1:18→1:48 [ui:Gizmo] · explained · scripted · 55% — The operator hover modes: live-hover thumbnails at fixed 640x360 vs. showing the operator's real current resolution.
- 1:48→2:21 [Layer2d] · explained · scripted · 80% — Pinning a [Layer2d] to the output still lets the output window drive its resolution; watch it re-render at 720p.
- 2:21→2:34 · explained · scripted · 65% — The other direction: resolution arriving "from the left" via an input, and why that matters.
- 2:34→3:05 [LoadImage] · explained · scripted · 70% — Loading a texture/image input so its native resolution (e.g. an odd 799x799 square frog) flows downstream.
- 3:05→3:42 [Layer2d][RenderTarget] · explained · scripted · 60% — Compositing the [Blob] onto an image and rendering into a [RenderTarget] while preserving the source aspect ratio.
- 3:42→4:13 [ui:OutputWindow] · in-depth · scripted · 85% — The magic resolution parameter: `0,0` means "do the right thing" — take the left image's size, else the output window's.
- 4:13→4:57 · in-depth · scripted · 75% — Forcing the output resolution with the magic `-1` value to ignore the incoming image and avoid letterbox borders.
- 4:57→5:36 · explained · scripted · 60% — Typing an explicit fixed resolution (very wide / very small) into the parameter and what that does to the dot.
- 5:36→6:14 · explained · scripted · 65% — Why radius-style parameters use image height as their unit and ignore width, so content is cropped left/right not top/bottom.
- 6:14→6:37 · passing · scripted · 40% — Closing call to join the Discord and drive the discussion.
