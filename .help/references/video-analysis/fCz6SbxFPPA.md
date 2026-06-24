---
video: fCz6SbxFPPA
type: tutorial
date: 2025-05-06
title: Get Positions in Screen Space — the GetScreenPos operator for screen-space labels
duration: 13:48
---

A scripted single-narrator tip tutorial introducing the new [GetScreenPos] operator, which converts a 3D world position into a flat screen-space coordinate so labels and overlays can be attached after the camera and post-processing. The tip walks through a reference scene, demonstrates [DrawCamGizmos] for finding where the camera points, then dissects the operator's bundled example — placing text/billboards at a tracked point.

## Mentions
- 0:00→0:50 [ui:OutputWindow] · explained · scripted · 60% — Why getting a screen-space position for a label after post-processing is a tricky problem worth solving.
- 0:50→1:19 [GetScreenPos] · passing · scripted · 80% — New operators introduced in TiXL 4.0.2 that make screen-space placement easier, plus their bundled examples.
- 1:19→2:24 [Locator] · explained · scripted · 75% — Touring the reference scene; pinning an output, and using a [Locator] to mark an interesting point on an abstract extruded shape.
- 1:39→1:43 [ui:Gizmo] · passing · scripted · 70% — Turning on gizmos to inspect the scene while building.
- 2:24→3:01 [Locator] · explained · scripted · 80% — Navigating the viewer (no camera yet) and matching a [Locator] precisely to the tip of a small detail.
- 3:01→3:37 [DrawCamGizmos] · in-depth · scripted · 90% — Using draw-camera-gizmos to visualize where the camera is pointing inside a busy scene, attached via a new group.
- 3:37→4:00 [ui:OutputWindow] · explained · scripted · 75% — Switching the output window mode to "viewer" so you can fly around and see the camera in 3D.
- 4:00→4:37 [Camera] · explained · scripted · 60% — Inspecting the camera in the viewer; offsetting/shaking it in world space to understand its framing.
- 4:37→5:35 [GetScreenPos] · explained · scripted · 85% — The core problem: getting a local-space position back out in screen space after looking through the (auto-picked) camera.
- 5:35→6:21 [GetScreenPos] · in-depth · scripted · 95% — How [GetScreenPos] works: it must be updated after the camera in the flow, and returns a 3-coordinate screen position with Z always zero (depth ignored).
- 6:21→8:06 [GetScreenPos] · in-depth · scripted · 90% — Tracing the result into a point/line; confirming the screen position stays flat in the view plane regardless of the source point's depth.
- 8:06→8:35 [Locator] · explained · scripted · 70% — When the [Locator]'s gizmo is handy versus just using its output position with the visual turned off.
- 8:35→9:04 [GetScreenPos] · explained · scripted · 80% — Duplicating the operator to reveal its bundled example and dragging it in to explore.
- 9:04→9:33 [Camera] · explained · scripted · 65% — The example's 3D scene with a camera; any camera type ([OrbitCamera] etc.) works with [GetScreenPos].
- 9:33→9:45 [GridPlane] · passing · scripted · 75% — The example's grid plane and red 3D objects forming the background scene.
- 9:45→9:57 [TorusMesh] · passing · scripted · 80% — The torus mesh used as a target in the bundled example.
- 9:57→10:06 [OscillateVec3] · explained · scripted · 80% — Driving the label's position around the torus in a circular path with oscillate-vec3.
- 10:06→10:24 [Vec3ToString] · explained · scripted · 70% — Converting the vector into text so the moving position can be shown as a label.
- 10:24→10:38 [OscillateVec2][Vec2ToVec3] · explained · scripted · 78% — Swapping in oscillate-vec2 as an easier-to-read alternative and lifting the 2D oscillation back to a 3D vector.
- 10:38→11:09 [Bloom] · explained · scripted · 85% — Rendering the scene to a texture and applying a bloom effect before the screen-space overlay stage.
- 11:09→11:48 [DrawBillboards] · explained · scripted · 55% — Using a (blue-tinted) layer as a billboard and plugging the [GetScreenPos] output into the group to position it.
- 11:48→13:05 [GetScreenPos] · in-depth · scripted · 80% — Building a point and a connecting line from the same screen-space position, then checking how the overlay tracks on screen.
- 13:05→13:48 [GetScreenPos] · passing · scripted · 50% — Wrap-up: acknowledging coordinate systems are confusing and recapping the journey to this solution.
