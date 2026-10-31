---
id: webcam-input
title: Webcam Input
scope: operators
tags: [user]
added: 2026-10-31
added-in-version: 4.4
prerequisites:
  - A camera is connected. Repeat the set with a USB webcam, which usually delivers MJPEG rather than raw frames.
  - No other program is using the camera.
related-help:
  - ../.help/docs/install/InstallLinux.md
---

Checks that `[WebcamInput]` finds cameras, delivers frames and recovers from a camera going away.

## Step: Default capture

**Action:**
Add a `[WebcamInput]`, pin it to the output and enable `Active`.

**Expected:**
- The camera image appears with correct colors within about a second.
- `Status` shows `Running at <width>x<height>`, with a height of 1080 or less.
- The frame rate of the editor does not drop noticeably.

## Step: Choosing a device and mode

**Action:**
Open the `InputDeviceName` dropdown and pick the camera. Set `ResolutionFpsType` to `Custom` and pick a resolution
and frame rate from their dropdowns.

**Expected:**
- The dropdowns list the cameras and modes of this machine.
- The image restarts in the chosen resolution without pressing `Reconnect`.

## Step: Transforming the image

**Action:**
Toggle `FlipVertically` and `FlipHorizontally`, then set `ApplyRotationData` to 30 and `Scale` to 0.5, 0.5.

**Expected:**
- `FlipVertically` mirrors left to right and `FlipHorizontally` turns the image upside down. These names are swapped
  on purpose, to keep existing projects looking the same.
- Each change applies while the camera keeps running, and the area outside the rotated image is transparent.

## Step: Sharing a camera

**Action:**
Add a second `[WebcamInput]`, enable `Active` on it and pin it to the output.

**Expected:**
- Its `Status` reports that the camera is already opened, and the first operator keeps running.
- After deleting or deactivating the first operator, the second one starts within a few seconds.

## Step: Unplugging

**Action:**
With a USB webcam running, unplug it, wait a few seconds and plug it back in.

**Expected:**
- `Status` reports that the camera is disconnected, and the editor keeps running.
- The image resumes on its own after the camera returns.

## Step: Exported executable

**Action:**
Export a project that shows a `[WebcamInput]` with `Active` enabled, and run the executable.

**Expected:**
- The camera image appears, and the executable exits cleanly while the camera is running.
