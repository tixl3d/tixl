---
id: render-bundles
title: Render Bundles for Post Effects
scope: rendering
tags: [rendering, post-effects]
added: 2026-11-15
added-in-version: 4.4
prerequisites:
  - A scene with a camera and shaded meshes, e.g. from `testing/PbrTests`.
---

[RenderScene] attaches its depth, normal and velocity buffers and the rendered camera to its color image. Post effects
like [ScreenSpaceLighting] pick them up with only the image connected, and pass them on to the next effect.

## Step: Bundle label

**Action:**
Render the scene with [RenderScene] and view its output.

**Expected:**
- The output window caption ends with `+DNV`, e.g. `1920x1080 RGBA16F +DNV`.
- A plain [RenderTarget] shows no suffix, also with depth or normals enabled.
- Image effects like [Blur] show no suffix.

## Step: ScreenSpaceLighting with only the image

**Action:**
Connect [RenderScene] to [ScreenSpaceLighting] with only the `Image` input connected. Compare with the same scene
wired explicitly ([RenderTarget] with depth, normal, velocity buffers and a camera reference connected).

**Expected:**
- Both look the same: ambient occlusion, reflections and anti-aliasing work and hold still when the camera moves.

## Step: Explicit inputs win

**Action:**
With [RenderScene] → [ScreenSpaceLighting], connect a different camera to `CameraReference` or a different texture to
`DepthMap`.

**Expected:**
- The connected value is used instead of the bundle's.

## Step: Innermost camera

**Action:**
Build mesh → camera 2 → camera 1 → [RenderScene] → [ScreenSpaceLighting].

**Expected:**
- The effect uses camera 2, the one the mesh was rendered with.

## Step: Forwarding

**Action:**
View the output of [ScreenSpaceLighting] fed by [RenderScene]. Then chain a second [ScreenSpaceLighting] after the first,
with only `Image` connected.

**Expected:**
- The first effect's output caption shows `+DNV`.
- The second effect works without extra connections.

## Step: Standalone effects

**Action:**
Connect [RenderScene] to [ScreenSpaceAO], [ScreenSpaceReflections] or [TXAA] with only `Image` connected, then chain
them (e.g. [ScreenSpaceAO] → [TXAA]).

**Expected:**
- Each works like the explicitly wired version, and their outputs show `+DNV`.

## Step: Moving occluders

**Action:**
Move [DrawPointsShaded] spheres over a floor drawn with [DrawMesh], rendered by [RenderScene] → [ScreenSpaceLighting].
Lower BlendFactor from 0.9 to 0.5.

**Expected:**
- Contact shadows left behind on the floor fade within a few frames; with 0.5 almost immediately. Still areas stay smooth.
- Spheres near the image border keep their shape and shading; their reflections and occlusion sit at the sphere.
- With the velocity buffer viewed, moving spheres show motion.

## Step: Missing bundle

**Action:**
Feed [ScreenSpaceLighting] from a plain [RenderTarget] with nothing else connected. Open the effect and select its
[HandleTextureBundle].

**Expected:**
- [HandleTextureBundle] shows a warning that names the missing depth or camera. Nothing crashes.
