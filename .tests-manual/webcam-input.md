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

## Step: Camera settings window

**Action:**
Trigger `OpenSettings`. Drag `Brightness` and `Contrast`, tick and untick `Auto` next to `White Balance`, switch to
the `Camera Control` tab and move `Zoom` or `Focus` if the camera offers them.

**Expected:**
- A window titled `<camera name> Properties` opens with the tabs `Video Proc Amp` and `Camera Control`, listing only
  the controls this camera reports, each with its own range. It looks the same on Windows, Linux and macOS (macOS
  cameras offer far fewer controls).
- Every change shows in the image right away. A slider is grayed out while its `Auto` box is ticked.
- `Cancel` puts the camera back the way it was when the window opened.
- `OK` stores the values in `CameraSettings`; Undo reverts that input. After saving, reopening the project and
  enabling `Active`, the camera comes back with the stored values.

## Step: Format in the settings window

**Action:**
In the settings window, open `Resolution`, pick a size, then open `Frame Rate` and pick a rate. Press `Cancel`.

**Expected:**
- Both dropdowns list only the modes this camera offers; `Frame Rate` lists the rates of the chosen size.
- Each pick restarts the image in that mode and sets `ResolutionFpsType`, `CustomResolution` and `CustomFps`.
  Undo reverts a pick.
- `Cancel` returns to the format the operator had when the window opened.

## Step: Connecting a camera while running

**Action:**
Start TiXL with the webcam unplugged and a `[WebcamInput]` set to that camera with `Active` enabled. Plug the camera
in. Then plug in a second camera and open the `InputDeviceName` dropdown.

**Expected:**
- The first camera starts within a few seconds, without a restart or `Reconnect`, and its stored camera settings
  are applied again.
- The second camera appears in the dropdown. On Windows and macOS, other running cameras may pause for about a
  second while the camera list is refreshed.

## Step: Driving controls from the graph

**Action:**
Pick `Pan`, `Zoom` or `Brightness` in the `Control1` dropdown, then drag `Control1Value` between 0 and 1. Turn off
`NormalizedControlValues` and type a value in the camera's own units.

**Expected:**
- Picking a control sets `Control1Value` to the camera's current value, so the image does not jump.
- Dragging the value moves the camera smoothly from the minimum to the maximum of its range.
- With `NormalizedControlValues` off, the value is used as typed. An unsupported control shows a warning in `Status`.

## Step: Sharing a camera

**Action:**
Add a second `[WebcamInput]`, enable `Active` on it and pin it to the output.

**Expected:**
- Its `Status` reports that the camera is already opened, and the first operator keeps running.
- After deleting or deactivating the first operator, the second one starts within a few seconds.

## Step: Pausing an unused camera

**Action:**
Enable `DeactivateWhenNotShowing`, then make the operator stop being rendered — for example, disconnect its
`Texture` output from what feeds the output window. Wait five seconds.

**Expected:**
- Close the camera in another application first: it must be able to open the camera once the operator has been
  idle for a few seconds.
- `Status` reports `Paused (not rendered)` while nothing renders the operator.
- Reconnecting `Texture` makes the image reappear within about a second.

## Known issue: rapid start/stop cycles and the camera list

**Status:** not reproducible any more, cause never identified — treat it as unexplained rather than fixed.

**Observed 2026-11-08:**
After roughly four or five rapid `Active` toggles the camera dropped out of SDL's camera list and capture
never recovered in that session: `Status` stayed `Error: Camera disconnected.`, reopening produced a stream
that was reported as gone, and a forced camera-support restart did not help either. The device also needed
a cooldown before it streamed reliably again, and the wedge persisted across freshly started editor
processes.

**Observed 2026-11-09:**
The same sequence — now automated, up to ten activations including six rapid cycles — passes repeatedly.
An A/B run with the reopen backoff disabled passed as well, so the backoff is *not* what changed the
outcome; the likeliest explanation is that the device itself had been left in a bad state by the earlier
runs. The two changes that did land in that window are the reopen backoff in `WebcamInput` (do not reopen a
failing camera more often than every 1, 2, 4, 5 s) and the per-stream locks plus open-outside-the-lock in
`SdlCameraProvider`; neither has been shown to address this.

Do not hammer a single camera with repeated test runs when investigating this.

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
