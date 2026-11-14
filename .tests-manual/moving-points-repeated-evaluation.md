---
id: moving-points-repeated-evaluation
title: Moving Points in Loops and Motion Blur
scope: rendering
tags: [rendering, points]
added: 2026-11-14
added-in-version: 4.4
prerequisites:
  - A project with animated points (e.g. [RadialPoints] moved by a time-driven [TransformPoints]) drawn with [DrawMovingPoints] and a non-zero VelocityStretch.
  - Playback is running.
---

[DrawMovingPoints] stretches points along their motion since the previous frame. When it is evaluated several
times per frame (inside [Loop], [RenderWithMotionBlur], [RenderToCubeMap], or shown in several windows), each
evaluation must compare against its own previous frame.

## Step: Moving points in a single pass

**Action:**
Look at the [DrawMovingPoints] output in the Output Window while playback runs.

**Expected:**
- Points are stretched along their direction of motion.
- Stopping playback keeps the stretched points as they were, without flicker.

## Step: Loop with an unused index

**Action:**
Put [DrawMovingPoints] inside a [Loop] with Count 3 and don't use the loop variable.

**Expected:**
- All three iterations draw the same stretched points; none of them loses its stretch.

## Step: Loop index moving the points

**Action:**
Use the loop variable (via [GetFloatVar]) to offset the points of each iteration, e.g. along X.

**Expected:**
- Each iteration shows its own copy with the same stretch as a single pass.
- No streaks connect one iteration's copy with another's.

## Step: Changing the loop count

**Action:**
While playback runs, change the [Loop] Count from 3 to 5, then back to 2.

**Expected:**
- New iterations appear without long streaks; at most one frame without stretch.
- No errors in the console.

## Step: Motion blur and cube maps unchanged

**Action:**
Render the single-pass setup inside [RenderWithMotionBlur], then inside [RenderToCubeMap].

**Expected:**
- Both look the same as before this change (compare with an older build if unsure).

## Step: Many repeat passes stay within memory

**Action:**
Place [KeepPreviousPointBuffer] with `Keep` on (not connected) on a large point set (1M points) inside
[RenderWithMotionBlur], and raise the pass count to 100.

**Expected:**
- GPU memory stops growing at about 512 MB for this op; the editor doesn't run out of memory.
- Passes beyond the budget show no motion instead of failing.

## Step: Re-enabling after a pause

**Action:**
Bypass the [DrawMovingPoints] chain for a few seconds while playback runs, then enable it again.

**Expected:**
- The points reappear without a one-frame streak across the screen.
