---
id: render-pass-state
title: Motion Blur and Cube Maps with Stateful Ops
scope: rendering
tags: [rendering, animation]
added: 2026-11-15
added-in-version: 4.4
prerequisites:
  - Playback is running.
---

[RenderWithMotionBlur] and [RenderToCubeMap] evaluate their scene several times per frame. Ops that keep state over time
([Damp], [Spring], [Ease], [HasTimeChanged], [AudioReaction], [DrawMovingPoints]) must advance only once per frame.

## Step: Damped motion inside motion blur

**Action:**
Move a mesh with a [Damp]ed or [Spring]-driven value inside [RenderWithMotionBlur].

**Expected:**
- The motion blur shows a smooth trail; the damped motion has the same speed as without motion blur.

## Step: Stateful ops after motion blur

**Action:**
Put a second, independent [Damp]-driven animation in the same output, evaluated after [RenderWithMotionBlur] (e.g. a
second branch of an [Execute] or [Group]).

**Expected:**
- The second animation keeps moving. (Before, it could freeze, because the loop left its pass number set for the rest
  of the frame.)

## Step: Cube map inside motion blur

**Action:**
Render a scene with [DrawMovingPoints] into [RenderToCubeMap] inside [RenderWithMotionBlur].

**Expected:**
- The cube map faces show consistent stretch on all faces; no face shows doubled or missing motion.

## Step: Graphs reading the legacy names

**Action:**
Inside [RenderWithMotionBlur], read `__MotionBlurPass` with [GetIntVar]; inside [RenderToCubeMap], read `CubeFace`.

**Expected:**
- Both show the pass number and face index as before.
