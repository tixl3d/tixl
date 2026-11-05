---
id: camera-pitch-stop
title: Camera Pitch Stop and Roll
scope: output-window
tags: [user]
added: 2026-11-05
added-in-version: 4.4
prerequisites:
  - A project with a 3D scene and a [Camera] operator, shown in the Output Window.
---

Checks that steering the camera in the output never flips the view upside down, and that a camera's roll is
respected by the viewer camera and by panning and orbiting.

## Step: Orbiting stops at the top

**Action:**
In the Output Window with the viewer camera, drag up with the left mouse button for a long way, past straight down onto the scene, then keep dragging.

**Expected:**
- The camera orbits over the target and stops when it looks straight down; further dragging doesn't flip the view.
- Dragging sideways there spins the image around the view axis without jumping.
- Dragging back down returns smoothly.

## Step: Looking around stops at the top

**Action:**
Drag with the middle mouse button (or Ctrl+left) up and down past straight up and straight down.

**Expected:**
- The view stops at straight up and straight down and never turns upside down.

## Step: Devices stop at the poles too

**Action:**
With a SpaceMouse or a game controller set to the camera, tilt the view up and down past the poles.

**Expected:**
- The view stops at straight up and down as with the mouse, without the earlier stop short of the pole.

## Step: The viewer camera keeps the scene camera's roll and field of view

**Action:**
Set the [Camera] operator's Roll to 30 and its field of view to 20, show it, then switch the output to the viewer camera (camera dropdown).

**Expected:**
- The viewer camera starts with the same tilted horizon and the same framing as the scene camera, without a jump.

## Step: Panning follows the rolled view

**Action:**
On the camera with Roll 30, pan with the right mouse button horizontally and vertically.

**Expected:**
- The scene follows the mouse along the tilted screen axes, not skewed.
