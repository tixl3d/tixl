---
video: f9E7lwUXfBM
type: tutorial
date: 2022-06-30
title: Tooll 3 Tip#003 - Resolution Magic
duration: 0:06:37
focusesOn: [ui:RenderSettings], [RenderTarget], [RequestedResolution], [SetRequestedResolution]
---

A short tip explaining how an image operator's resolution is resolved: the "magic" 0×0 default pulls resolution from an incoming texture if present, otherwise from the output window, and how -1 or an explicit size overrides that.

## Mentions
- 0:25→0:48 [Blob] · explained · scripted · Example · 88% — A handy starting image effect for demonstrating how resolution flows; its drawn circle stays circular regardless of the requested aspect ratio.
- 0:31→1:02 [ui:OutputWindow] · explained · scripted · Concept · 90% — In its default "fill" mode the output window size sets the render resolution of whatever is pinned to it, and aspect ratio is preserved so circular content stays circular.
- 1:05→1:18 [ui:OutputSettings] · explained · scripted · Tip · 85% — Override the inferred output size with a fixed resolution (Full HD, 4K, 720p) when you need predictable, reproducible dimensions instead of whatever the window happens to be.
- 1:22→1:35 [ui:Graph] · passing · scripted · Tip · 70% — Live-hover previews always render at a fixed 640×360 thumbnail size, whereas the "current state" hover shows the operator's true resolution.
- 1:48→2:21 [Layer2d] · explained · scripted · Gotcha · 78% — Even once it is no longer a flat image, its render resolution is still driven by the output it is pinned to, so changing the output size re-renders it at the new resolution.
- 2:32→3:14 [LoadImage] · explained · scripted · Example · 85% — A loaded texture forces its own pixel dimensions (e.g. a square 799×799 source) downstream, which is why effects suddenly inherit a non-standard incoming resolution.
- 3:23→3:40 [RenderTarget] · explained · scripted · Example · 80% — Rendering a fitted layer back into a target re-establishes a chosen aspect ratio (16:9), letting odd source resolutions be normalised before further compositing.
- 3:27→3:41 [RenderTarget] · explained · scripted · Concept · 78% — Wrapping content in one bakes it to a fixed-size texture, so its own size — not whatever feeds in upstream — becomes the resolution everything downstream inherits, the clean way to pin a pipeline to a known dimension.
- 3:42→4:13 [RequestedResolution] · in-depth · scripted · Concept · 80% — The 0×0 "magic" default means resolve-on-demand: prefer an incoming image's size, else fall back to the output window's request — which is why image effects usually need no explicit size at all.
- 4:13→6:00 [SetRequestedResolution] · explained · scripted · Parameters · 75% — Force a fixed size downstream to override the inherit-from-context behaviour: -1 snaps to the output window (filling the frame, accepting stretch), or an explicit width×height locks exact dimensions regardless of what flows in.
- 3:42→4:13 [ui:RenderSettings] · in-depth · scripted · Parameters · 90% — The default 0×0 resolution is a sentinel meaning "do the right thing": take an incoming image's resolution if one exists, otherwise the output window's requested size.
- 4:13→4:57 [ui:RenderSettings] · explained · scripted · Gotcha · 82% — Setting resolution to -1 forces the output window's resolution and ignores any incoming image size, useful when you want the effect to fill the frame and avoid letterbox borders even at the cost of stretching.
- 4:57→6:00 [ui:RenderSettings] · explained · scripted · Tip · 75% — Typing an explicit resolution (very wide and short, etc.) overrides everything; radius-style parameters then key off image height as their unit so width changes crop sideways rather than top/bottom.
