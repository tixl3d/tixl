---
id: velocity-buffer
title: Velocity Buffer and TXAA
scope: rendering
tags: [rendering, postfx]
added: 2026-11-14
added-in-version: 4.4
prerequisites:
  - A scene with a [Camera] and a few meshes ([DrawMesh]) rendered by [RenderTarget] into [TXAA] (DepthMap connected).
---

[RenderTarget] can render a velocity buffer, which [TXAA] reads to follow moving objects. [DrawMesh] writes velocity;
draw operators that don't fall back to the camera-only reprojection.

## Step: Velocity buffer output

**Action:**
On the [RenderTarget], enable `WithVelocityBuffer` and look at its `VelocityBuffer` output.

**Expected:**
- The output is a black (all zero) texture at the render target's resolution.
- No errors in the console.

## Step: TXAA unchanged with an empty velocity map

**Action:**
Connect `VelocityBuffer` to the `VelocityMap` input of [TXAA] and orbit the camera, then disconnect it again.

**Expected:**
- The TXAA output looks the same connected and disconnected: same sharpness, same ghosting on moving objects.

## Step: Normals with and without velocity

**Action:**
With `WithVelocityBuffer` on, toggle `WithNormalBuffer` off and on and look at `NormalBuffer` and `ColorBuffer`.

**Expected:**
- `ColorBuffer` looks the same in both states.
- With `WithNormalBuffer` on, `NormalBuffer` shows the scene's normals as before.

## Step: Multisampling

**Action:**
Set `Multisampling` to 4 samples with both buffers enabled.

**Expected:**
- `VelocityBuffer` and `NormalBuffer` still have the render target's resolution; no errors in the console.

## Step: Moving mesh in the velocity buffer

**Action:**
Rotate the mesh with an animated [Rotate] (or move it with [Transform]) while playback runs, and look at
`VelocityBuffer`.

**Expected:**
- The moving mesh shows colored velocity (red/green by direction); the static background stays black.
- With playback stopped, the mesh's velocity drops to near zero.

## Step: TXAA on a moving mesh

**Action:**
With the mesh moving and the camera static, compare [TXAA] with `VelocityMap` connected and disconnected.

**Expected:**
- Connected: the moving mesh keeps its edges smooth without trailing ghosts.
- Disconnected: the moving mesh shows the ghosting or blur seen before.

## Step: Screen space lighting on a moving mesh

**Action:**
Feed the same [RenderTarget] (color, depth, normals) into [ScreenSpaceLighting] with the mesh moving and the camera
static. Compare `VelocityMap` connected and disconnected, looking at the `Occlusion` output and the final image.

**Expected:**
- Connected: the occlusion under and around the moving mesh follows it and stays converged (smooth, little noise).
- Disconnected: the moving mesh's occlusion is noisier or trails behind, as before.
- Static parts of the scene look the same in both cases.

## Step: Instances at moving points

**Action:**
Draw a mesh with [DrawMeshAtPoints] at points that move (e.g. [SpherePoints] → [TransformPoints] with an animated
Rotation), into a [RenderTarget] with `WithVelocityBuffer`. Look at `VelocityBuffer`, then stop the animation.

**Expected:**
- While the points move, the instances show velocity; neighbouring instances moving together show a smooth field.
- With the points still, the velocity of the instances is zero.

## Step: Point draws

**Action:**
Animate points (e.g. a rotating [RadialPoints] or a particle simulation) and draw them with [DrawPoints],
[DrawBillboards], [DrawMovingPoints], [DrawMeshAtPoints2] and [DrawMeshChunksAtPoints] in turn, rendered by
[RenderScene] → [MotionBlur]. View the VelocityBuffer output as well.

**Expected:**
- Each draw blurs along its motion and shows motion in the velocity buffer; still points show none.
- Respawning particles don't streak across the screen.
- Blended sprites over a mesh keep the mesh's shading in [ScreenSpaceLighting] (normals behind them stay intact).
