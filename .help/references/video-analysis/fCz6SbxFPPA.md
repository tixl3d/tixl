---
video: fCz6SbxFPPA
type: tutorial
date: 2025-05-06
title: TiXL Tip#23 - Get Positions in Screen Space
duration: 0:13:48
focusesOn: [GetScreenPos]
---

A tip-of-the-day walkthrough of the new [GetScreenPos] operator (TiXL 4.0.2), which converts a world-space locator into a 2D screen-space coordinate so a label or billboard can be pinned over a point in a post-processed 3D scene.

## Mentions
- 0:05:35→0:06:25 [GetScreenPos] · in-depth · scripted · Concept · 90% — Reads a transform that lives *after* the camera in the evaluation flow and reports where that point lands on screen; the returned Z is always 0 because depth is intentionally discarded, leaving a flat 2D position to anchor overlays.
- 0:06:25→0:08:00 [GetScreenPos] · explained · scripted · Example · 84% — The output position drives the first point of a connected line, so the on-screen marker follows the 3D target as the camera moves; switching the viewer to a flat/image view confirms the coordinate stays in the view plane.
- 0:08:30→0:11:50 [GetScreenPos] · explained · scripted · Example · 82% — Its built-in example renders the 3D scene to a texture, then feeds the screen-space output into a separate billboard layer so a label sits in screen space on top of the post-processed image.
- 0:09:45→0:10:40 [OscillateVec3] [OscillateVec2] · passing · scripted · Comparison · 70% — A 2D oscillator converted up to 3D drives a point along a circular path and reads more clearly than the 3D variant for a simple planar motion.
- 0:03:10→0:04:32 [DrawCamGizmos] · explained · scripted · Tip · 80% — Attaching it near a camera and viewing the scene from a second vantage point makes the camera's frustum visible, which is how you find where a hidden camera is actually pointing in a cluttered scene.
- 0:01:39→0:03:01 [Locator] · explained · scripted · Tip · 76% — Drop one at a point of interest to get a draggable gizmo and a world-space position output; the gizmo can be turned off so only the position output is used to drive other operators.
- 0:10:48→0:10:57 [Bloom] · passing · scripted · Example · 60% — Applied as a post-process on the rendered scene texture before the screen-space label layer is composited on top.
- 0:00:43→0:05:06 [ui:OutputWindow] · passing · scripted · Tip · 62% — Switching its mode to a free viewer lets you orbit independently of the scene camera to inspect where that camera sits and points.
