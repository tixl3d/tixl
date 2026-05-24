---
id: swift-cam-device
title: SwiftCamDevice — Swift Imaging camera capture
scope: io-video
tags: [hardware, essential]
prerequisites:
  - A Swift Imaging USB camera is on hand (e.g. Swiftcam_SC1003).
  - The vendor's Swift Imaging app is closed (only one process can hold the camera).
  - For most steps, swiftcam.dll (x64) is installed at `%LOCALAPPDATA%\TiXL\NativeDeps\swiftcam.dll`. The first step verifies the missing-DLL path.
related-help:
  - ../.help/docs/using/SwiftCamSetup.md
---

End-to-end verification of the `[SwiftCamDevice]` operator: missing-DLL UX, device
discovery, streaming, manual exposure, ROI changes, hot-unplug recovery.

## Step: Reacting to a missing DLL

**Action:**
Before starting, make sure `swiftcam.dll` is **not** at
`%LOCALAPPDATA%\TiXL\NativeDeps\swiftcam.dll` (rename or move it temporarily).

Drop a `[SwiftCamDevice]` operator into a composition and open its **Device Name**
dropdown.

**Expected:**
- The dropdown shows a single entry beginning with `swiftcam.dll not found`.
- The operator's **Status** output and its node badge indicate an error level.
- The editor does not crash and there's no exception spam in the Console.

## Step: Recovering after the DLL is installed

**Action:**
With the previous step's missing-DLL state still showing, restore `swiftcam.dll`
(x64) at `%LOCALAPPDATA%\TiXL\NativeDeps\swiftcam.dll` and trigger the operator's
**Reconnect** input.

**Expected:**
- After up to one second, the **Device Name** dropdown lists the connected
  camera by its product name (e.g. `Swiftcam_SC1003`).
- The Status no longer shows the missing-DLL message.

## Step: Starting the stream

**Action:**
With the camera detected, pick it from the **Device Name** dropdown, toggle
**Active** on, and wire the operator's **Texture** output into a viewer
(`[Layer2d]`, `[Display]`, or anything that accepts a Texture2D).

**Expected:**
- **Status** shows `Streaming.` with a success badge.
- A live image appears in the viewer (the first frame can take 1–2 seconds).
- The **Resolution** output reports the streaming resolution.
- **UpdateCount** increments while frames are flowing.

## Step: Switching to a smaller resolution preset

**Action:**
While streaming at the default `Resolution Index = 0`, open the **Resolution
Index** dropdown and pick a smaller preset such as `1: 1832x1374`.

**Expected:**
- The stream restarts within roughly two seconds.
- **Resolution** output and the viewport image switch to the smaller dimensions.
- Frame rate is noticeably higher than at full sensor.

## Step: Manual exposure control

**Action:**
While streaming, toggle **Auto Exposure** off, then move **Exposure** between
a low value (around 1 ms) and a high one (around 100 ms).

**Expected:**
- At low Exposure the image is dark; at high Exposure it brightens.
- Frame rate visibly increases at low Exposure.
- The changes apply during streaming — no restart.

## Step: Applying a region of interest

**Action:**
While streaming with `Roi Resolution = (0, 0)` (full sensor), set
**Roi Resolution** to `(1920, 1080)`.

**Expected:**
- After roughly one second the viewport texture switches to a 1920×1080 crop
  (or close, snapped to multiples of 4).
- Status briefly shows `Starting…` during the restart and then returns to
  `Streaming.`.
- The editor stays responsive throughout — no ImGui freeze.

## Step: Moving the ROI window

**Action:**
With the ROI active at `(1920, 1080)`, move **Roi Alignment** to `(-1, -1)`,
then `(1, 1)`, and finally back to `(0, 0)`.

**Expected:**
- Each change triggers a stream restart of about one second — the SDK doesn't
  accept ROI changes mid-stream cleanly.
- `(0, 0)` shows the centered crop, `(-1, -1)` the top-left of the sensor, and
  `(1, 1)` the bottom-right.
- The editor stays responsive throughout.

## Step: Resetting the ROI

**Action:**
With the ROI still active, set **Roi Resolution** back to `(0, 0)`.

**Expected:**
- After roughly one second the viewport returns to the full preset resolution.

## Step: Enabling verbose log messages

**Action:**
While streaming, open the editor's Console window and toggle **Log Messages** on.

**Expected:**
- The Console begins receiving `SwiftCamDevice: event EVENT_IMAGE` lines (one
  per frame) and `put_Option(…)`-style traces.
- Each log line is clickable and selects the operator instance.

## Step: Disabling verbose log messages

**Action:**
With the verbose logs flowing, toggle **Log Messages** off again.

**Expected:**
- Per-frame and per-step log lines stop within one frame.
- Errors and one-time lifecycle events (start, stop, first frame, disconnect)
  still log.

## Step: Recovering from a USB unplug

**Action:**
While streaming, unplug the camera's USB cable. Wait until you see
`Camera disconnected` or `Camera reported an error` in the **Status**, then
plug the cable back in.

**Expected:**
- Within roughly two seconds of the unplug, **Status** shows a warning or error
  and the operator stops trying to deliver frames.
- The Console logs `EVENT_DISCONNECTED` (or a related error) once at warning
  level.
- After replug, the operator auto-reconnects within a few seconds without
  manual intervention. **Status** returns to `Streaming.` and frames resume.

## Step: Stopping the stream cleanly

**Action:**
While streaming, toggle **Active** off.

**Expected:**
- Within one frame **Status** shows `Inactive.` with a notice badge.
- **UpdateCount** stops incrementing.
- Re-toggling Active resumes streaming within one to two seconds.

## Step: Deleting the operator while streaming

**Action:**
While streaming, select the `[SwiftCamDevice]` operator and press `Delete`
(or right-click → Delete).

**Expected:**
- The operator is removed from the graph.
- No error in the Console — the camera handle releases cleanly via `Dispose`.
- Spawning a fresh `[SwiftCamDevice]` and toggling Active streams the same
  camera again without needing an editor restart.
