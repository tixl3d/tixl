---
id: swift-cam-device
title: SwiftCamDevice — Swift Imaging camera capture
scope: io-video
tags: [hardware, essential]
prerequisites:
  - A Swift Imaging USB camera is on hand (e.g. Swiftcam_SC1003).
  - The vendor's Swift Imaging app is **closed** (only one process can hold the camera).
  - For most steps, swiftcam.dll (x64) is installed at %LOCALAPPDATA%\TiXL\NativeDeps\swiftcam.dll. The first step verifies the missing-DLL path.
related-help:
  - ../.help/docs/using/SwiftCamSetup.md
---

End-to-end verification of the `SwiftCamDevice` operator: missing-DLL UX, device discovery, streaming, manual exposure, ROI changes, hot-unplug recovery.

## Step: Verify missing-DLL message

**Context:** swiftcam.dll is **not** present at `%LOCALAPPDATA%\TiXL\NativeDeps\swiftcam.dll` (rename or move it temporarily).
**Action:**
- Drop a `[SwiftCamDevice]` operator into a composition.
- Open the **Device Name** dropdown.

**Expected:**
- The dropdown shows a single entry beginning with `swiftcam.dll not found`.
- The operator's **Status** output and badge indicate an error level.
- The editor does not crash, no exception spam in the log.

## Step: Recover after installing the DLL

**Context:** From the previous step, with the missing-DLL message visible.
**Action:**
- Restore swiftcam.dll (x64) at `%LOCALAPPDATA%\TiXL\NativeDeps\swiftcam.dll`.
- Trigger the operator's **Reconnect** input.

**Expected:**
- After up to one second, the **Device Name** dropdown lists the connected camera by its product name (e.g. `Swiftcam_SC1003`).
- The Status no longer shows the missing-DLL message.

## Step: Pick the device and start streaming

**Context:** Camera detected, dropdown open.
**Action:**
- Pick the camera from the dropdown.
- Toggle **Active** on.
- Wire **Texture** into a viewer (`[Layer 2 d]`, `[Display]`, or any operator that accepts a Texture2D).

**Expected:**
- **Status** shows `Streaming.` with a success badge.
- A live image appears in the viewer (may take 1–2 seconds for the first frame).
- The **Resolution** output reports the streaming resolution.
- **UpdateCount** increments while frames flow.

## Step: Pick a smaller resolution preset

**Context:** Streaming at the default `Resolution Index = 0`.
**Action:**
- Open the **Resolution Index** dropdown.
- Pick a smaller preset (e.g. `1: 1832x1374`).

**Expected:**
- The stream restarts within ~2 seconds.
- **Resolution** output and the viewport image switch to the smaller dimensions.
- Frame rate is noticeably higher than at full sensor.

## Step: Disable auto exposure and adjust manually

**Context:** Streaming.
**Action:**
- Toggle **Auto Exposure** off.
- Move **Exposure** between low (e.g. 1 ms) and high (e.g. 100 ms).

**Expected:**
- With low Exposure, the image is dark; high Exposure brightens it.
- Frame rate visibly increases at low Exposure.
- No restart; changes apply during streaming.

## Step: Apply a ROI

**Context:** Streaming with `Roi Resolution = (0, 0)` (full sensor).
**Action:**
- Set **Roi Resolution** to `(1920, 1080)`.

**Expected:**
- After ~1 second, the viewport texture switches to a 1920×1080 (or close, snapped to multiples of 4) crop.
- The Status briefly shows `Starting…` during the restart, then returns to `Streaming.`.
- The editor remains responsive throughout (no ImGui freeze).

## Step: Move the ROI window

**Context:** ROI active at `(1920, 1080)` from the previous step.
**Action:**
- Move **Roi Alignment** to `(-1, -1)`, then `(1, 1)`, then back to `(0, 0)`.

**Expected:**
- Each change triggers a stream restart (~1 second) — the SDK doesn't accept ROI changes mid-stream cleanly.
- `(0, 0)` shows the centered crop; `(-1, -1)` the top-left of the sensor; `(1, 1)` the bottom-right.
- The editor remains responsive throughout.

## Step: Reset ROI

**Context:** ROI active from previous step.
**Action:**
- Set **Roi Resolution** back to `(0, 0)`.

**Expected:**
- After ~1 second, the viewport returns to the full preset resolution.

## Step: Enable verbose log messages

**Context:** Streaming.
**Action:**
- Open the editor's Console window.
- Toggle **Log Messages** on.

**Expected:**
- The Console begins receiving `SwiftCamDevice: event EVENT_IMAGE` lines (one per frame) and `put_Option(...)`-style traces.
- Each log line is clickable and selects the operator instance.

## Step: Disable verbose log messages

**Context:** Verbose logs flowing.
**Action:**
- Toggle **Log Messages** off.

**Expected:**
- Per-frame and per-step log lines stop within one frame.
- Errors and one-time lifecycle events (start, stop, first frame, disconnect) still log.

## Step: Recover from a USB unplug

**Context:** Streaming.
**Action:**
- Unplug the camera's USB cable.
- Wait until you see `Camera disconnected` or `Camera reported an error` in the **Status**.
- Plug the cable back in.

**Expected:**
- Within ~2 seconds of unplug, **Status** shows a warning/error and the operator stops trying to deliver frames.
- The Console logs `EVENT_DISCONNECTED` (or a related error) once at warning level.
- After replug, the operator auto-reconnects within a few seconds without manual intervention. **Status** returns to `Streaming.` and frames resume.

## Step: Stop streaming cleanly

**Context:** Streaming.
**Action:**
- Toggle **Active** off.

**Expected:**
- Within one frame, **Status** shows `Inactive.` with a notice badge.
- **UpdateCount** stops incrementing.
- Re-toggling Active resumes streaming within ~1–2 seconds.

## Step: Delete the operator while streaming

**Context:** Streaming.
**Action:**
- Select the `[SwiftCamDevice]` operator.
- Press `Delete` (or right-click → Delete).

**Expected:**
- The operator is removed from the graph.
- No error in the Console (camera handle releases cleanly via `Dispose`).
- Re-instancing a fresh `[SwiftCamDevice]` and toggling Active streams the same camera again without needing the editor restart.
