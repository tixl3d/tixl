---
id: motion-blur
title: Motion Blur from Velocity
scope: rendering
tags: [rendering, post-effects]
added: 2026-11-15
added-in-version: 4.4
prerequisites:
  - Playback is running.
---

[MotionBlur] blurs each pixel along its motion from the velocity buffer of [RenderScene], and falls back to the camera
motion where no velocity was written.

## Step: Moving object, still camera

**Action:**
Animate a mesh ([DrawMesh]) moving sideways in front of a still camera. Render with [RenderScene] → [MotionBlur], only
`Image` connected.

**Expected:**
- The mesh blurs along its motion; the still background stays sharp.
- The blur extends beyond the mesh's outline; the background shows through the blurred edges.
- Stopping playback removes the blur.

## Step: Shutter and MaxBlur

**Action:**
Change Shutter from 0.5 to 1 and 0, and lower MaxBlur to 4.

**Expected:**
- 1 doubles the blur length, 0 shows the image unchanged.
- MaxBlur 4 limits fast motion to short streaks.

## Step: Camera motion

**Action:**
Orbit the camera around a still scene.

**Expected:**
- Everything blurs according to its screen motion; the center of rotation stays sharp.
- No visible square tile pattern at the edges of blurred areas.

## Step: Plain RenderTarget

**Action:**
Use a [RenderTarget] without velocity buffer, with DepthMap and CameraReference connected.

**Expected:**
- Camera motion still blurs; moving objects in front of a still camera stay sharp.

## Step: Old projects

**Action:**
Open `Lib/render/postfx/MotionBlurExample` and `MotionBlurExample2`.

**Expected:**
- They load without errors; the blur follows the camera motion.

## Step: Order with TXAA

**Action:**
[RenderScene] → [TXAA] → [MotionBlur], then swap to [MotionBlur] → [TXAA].

**Expected:**
- The first order is stable. The second may smear or flicker, because TXAA's history receives the blurred image.
