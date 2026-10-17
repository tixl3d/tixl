---
id: ndi-linux
title: NDI input and output on Linux
added: 2026-10-17
added-in-version: 4.4
scope: io-video
tags: [linux, hardware, network]
prerequisites:
  - TiXL runs natively on Linux (Vulkan).
  - A second NDI endpoint on the same network or machine, e.g. OBS Studio with the DistroAV plugin, or NDI Tools under Wine.
  - The NDI runtime (`libndi.so.6`) is installed, e.g. `ndi-sdk` from the AUR, and `avahi-daemon` is running. The first step verifies the missing-runtime path.
related-help:
  - ../.help/docs/install/InstallLinux.md
  - ../.help/docs/install/PlatformSupport.md
---

End-to-end check of `[NdiInput]` and `[NdiOutput]` on Linux, where the NDI runtime is installed by the
user rather than shipped with TiXL. The first step runs without the runtime, the rest with it.

## Step: Reporting a missing runtime

**Action:**
Start TiXL with the NDI runtime hidden, e.g. `NDI_RUNTIME_DIR_V6=/nonexistent` on a machine where
`libndi.so.6` is not on the library path. Add an `[NdiOutput]` and an `[NdiInput]` to an empty project and
connect any texture to the output.

**Expected:**
- Both operators show "NDI runtime not found" with install hints in their status, without crashing.
- `[NdiOutput]` still passes its input texture through to its output.
- The log carries no exception, only the status message.

## Step: Sending a texture

**Action:**
With the runtime installed, restart TiXL, open the project again and give `[NdiOutput]` a sender name.
On the second endpoint, look for a source named `<hostname> (<sender name>)` and connect to it.

**Expected:**
- The source appears within a few seconds.
- The receiver shows the texture, moving in sync with TiXL, at the frame rate set on the operator.
- With *EnableAlpha* off, the stream arrives as UYVY (shown as such in the receiver's stream info, if it has one); with it on, as BGRA/RGBA with transparency.

## Step: Receiving a stream

**Action:**
Make the second endpoint send a stream (OBS: start the NDI output; NDI Tools: the test pattern generator).
On `[NdiInput]`, open the *SourceName* dropdown and pick that source.

**Expected:**
- The dropdown lists the source by its `MACHINE (name)` label.
- After selecting it, the operator's output texture shows the stream and updates continuously.
- The operator's status is empty (success); no "skipping frame" message stays on permanently at 30 fps or more.

## Step: Changing the source resolution

**Action:**
While `[NdiInput]` is connected, change the resolution of the sending stream (OBS: change the canvas size
and restart the output).

**Expected:**
- The output texture switches to the new size without a restart.
- No Vulkan validation errors or crash in the log.

## Step: Streaming from the output setup

**Action:**
Open the output setup, add an NDI stream as an output target, and connect to it from the second endpoint.

**Expected:**
- The stream appears and behaves like the one from `[NdiOutput]`, including frame rate and alpha options.
- Removing the stream target makes the source disappear from the receiver's list.
