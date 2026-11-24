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
- The *Display* line shows the display's refresh rate as the system reports it, e.g. `120.00 Hz display`.
- On a 48 Hz panel it reads 48 Hz, not 60.
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

## Step: Lock Animation Step

**Action:**
Add a [LastFrameDuration] op and show its value (e.g. with [FloatToString] and a text layer), and a [Damp] or particle
setup that moves continuously. Check that *Lock Animation Step* is on by default. With *Frame Rate* at
*Match Display*, toggle it, then cause a few dropped frames (e.g. drag a heavy parameter, or briefly resize a window).

**Expected:**
- Without drops [LastFrameDuration] shows exactly one refresh period (8.333 ms at 120 Hz) on every frame in both
  settings, with no fluctuation in the last digits.
- Off: on a dropped frame it shows whole refreshes more (16.667, 25.0 ms) and damped motion keeps its speed.
- On: it stays at 8.333 ms through drops; damped and particle motion lags slightly on a drop instead of jumping ahead.
- Keyframed animation and audio stay in sync with real time in both settings.

## Step: Settings → Rendering

**Action:**
Open *Settings → Rendering* next to the Performance window and change *Frame Rate* and *Lock Animation Step* in one of them.

**Expected:**
- Both show the same values and follow each other's changes.
- The category also lists *Max Fill Resolution*, *Suspend rendering when hidden*, the GPU memory settings and the shader options; *Settings → Interface* and *Settings → Projects* no longer do.
- Below the frame-rate settings a hint suggests a fullscreen output against dropped frames.

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

## Step: Exports ignore the display's pace

**Action:**
With VSync on, export a video of a light scene from the [ui:RenderSettings|Render To File] window with a *Custom* range. Then start a *Continuous* capture with the *Realtime* clock, record for a few seconds and stop it.

**Expected:**
- The range export runs faster than the display's refresh rate if the scene and encoder allow it, and the file has the requested frame count.
- During the realtime capture the frame time stays at one refresh period.
- *Use VSync* stays checked throughout, and the frame rate returns to normal after each export.
