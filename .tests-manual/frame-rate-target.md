---
id: frame-rate-target
title: Frame Rate Setting and Display Pacing
added: 2026-10-12
added-in-version: 4.3
scope: editor-shell
tags: [rendering]
prerequisites:
  - A display running at 120 Hz (or another rate that is a multiple of 60), with VSync on.
  - A project with an animation that moves continuously, e.g. a rotating object driven by playback time.
  - For the last step, a second display (projector or monitor) and an output bound to it in Output Setup.
---

The Frame Rate setting renders at a fraction of the display's refresh rate, and the first bound output paces
the frame loop. These steps check that the frame rate follows the setting, that animations keep their speed,
and that the measured refresh rate is the real one.

## Step: Refresh rate is measured from the display

**Action:**
Open the Performance window (click the frame graph in the app bar) with *Frame Rate* set to *Match Display*.

**Expected:**
- The *Display* line shows the display's real refresh rate, e.g. `119.9 Hz measured`, within a few seconds.
- On a 48 Hz panel it reads about 48 Hz, not 60.
- Frame time is about one refresh period (8.3 ms at 120 Hz).

## Step: Lower frame rates

**Action:**
Set *Frame Rate* to *60 fps*, then *30 fps*, then *50 fps*, while playback runs.

**Expected:**
- At 60 fps the frame time settles at about 16.7 ms and the *Display* line shows a 60 fps target.
- At 30 fps it settles at about 33.3 ms.
- 50 fps on a 120 Hz display gives a 60 fps target, since only whole refreshes are possible.
- The animation keeps the same speed in every setting; only its smoothness changes.
- Interaction (dragging a slider, panning the graph) has no noticeable lag beyond the lower rate.

## Step: Frame Rate needs VSync

**Action:**
Turn *Use VSync* off with *Frame Rate* still at 60 fps, then turn it back on.

**Expected:**
- With VSync off the frame rate is no longer limited by the setting; the *Display* line's target equals the refresh rate.
- Neither state shows growing input lag, and *Presentation* reports only a few queued frames.
- With VSync on again, the 60 fps rate returns.

## Step: A bound output paces the loop

**Action:**
Bind an output to the second display, set *Frame Rate* to *Match Display*, and play the animation.

**Expected:**
- The log shows `Frame pacing now follows 'TiXL Output N'`.
- The *Display* line shows the second display's refresh rate.
- Motion on the second display is smooth; the editor window may skip frames if its screen runs at a different rate.
- Unbinding the output logs pacing following the editor window again.
