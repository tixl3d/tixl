---
id: omt-streaming
title: OMT Streaming (Open Media Transport)
scope: io-video
tags: [network, cross-platform]
added: 2026-11-07
added-in-version: 4.4
prerequisites:
  - Two machines on the same network, ideally one Windows and one Linux, both running this TiXL build.
  - On Linux, avahi-daemon is running for discovery (systemctl status avahi-daemon); steps that use omt://host:port work without it.
  - The firewall allows TCP ports 6400-6600 and mDNS (UDP 5353) between the machines.
---

Covers `[OmtOutput]`, `[OmtInput]` and OMT streams in the output setup, across machines and operating systems.
"Machine A" sends, "machine B" receives; run the set once with Windows → Linux and once with Linux → Windows.

Without the editor, `Tests/OmtProbe` checks the same link from a terminal:
`dotnet run --project Tests/OmtProbe -- send` on one machine,
`dotnet run --project Tests/OmtProbe -- list` and `-- receive "MACHINE (OmtProbe)"` on the other.

## Step: Sending a texture

**Action:**
On machine A, connect `[TorusMesh]` → `[DrawMesh]` → `[RenderTarget]` → `[OmtOutput]`, keep the defaults (SenderName `TiXL`, FrameRate 30) and pin the output.

**Expected:**
- `[OmtOutput]` shows no error.
- Within a second, its SourceAddress output reads `MACHINE-A (TiXL)` (machine name in capitals).
- The rendered frame time does not change noticeably compared to the same graph without `[OmtOutput]`.

## Step: Discovering the source

**Action:**
On machine B, add `[OmtInput]` and open its SourceName dropdown.

**Expected:**
- `MACHINE-A (TiXL)` is listed (it can take a few seconds to appear the first time).
- After selecting it, the output shows the spinning torus from machine A, moving smoothly.
- The status of `[OmtInput]` is clear (no warning).

## Step: Alpha

**Action:**
On machine A, enable EnableAlpha on `[OmtOutput]` and give the render target a transparent background. On machine B, view the `[OmtInput]` output over a checkerboard.

**Expected:**
- The background is transparent on machine B; the torus is not.
- Red and blue are not swapped, with both B8G8R8A8 and R8G8B8A8 render targets.

## Step: Renaming and restarting the sender

**Action:**
On machine A, change SenderName to `Stage`, wait for machine B's dropdown, then change it back to `TiXL`.

**Expected:**
- Neither machine's frame loop stutters during the rename.
- `MACHINE-A (Stage)` appears in machine B's list and `MACHINE-A (TiXL)` disappears, then the reverse.
- `[OmtInput]` on machine B shows "Waiting for ..." while its source is gone and resumes on its own once `TiXL` is back, without pressing TriggerReconnect.

## Step: Losing the network

**Action:**
While receiving, unplug the network cable (or disable Wi-Fi) on machine A for about 20 seconds, then reconnect it.

**Expected:**
- Both editors stay responsive the whole time; nothing freezes or crashes.
- Machine B keeps the last frame and reports that it is waiting; it resumes on its own after the network returns.

## Step: Connecting by address

**Action:**
On machine B, type `omt://<machine A's IP>:6400` into `[OmtInput]`'s SourceName (6400 is the first sender's port; the sender logs its address at debug level).

**Expected:**
- The stream is received even with discovery unavailable (e.g. avahi-daemon stopped on Linux).

## Step: Deleting operators while streaming

**Action:**
While a stream is running, delete `[OmtInput]` on machine B, undo, then delete `[OmtOutput]` on machine A.

**Expected:**
- Deleting and undoing are instant; the frame loop does not hitch.
- After deleting `[OmtOutput]`, the source disappears from machine B's dropdown within a few seconds.

## Step: Streaming from the output setup

**Action:**
On machine A, open the output setup, add a stream sender with the `+` in LOCAL BINDINGS, choose `OMT`, name it `Wall`, and drag an output onto it. Enable alpha and set 60 fps in its settings.

**Expected:**
- `OMT` is offered next to `Spout` and `NDI` in the Add Stream Sender menu.
- `MACHINE-A (Wall)` can be received with `[OmtInput]` on machine B, showing the composited output.
- Unbinding the output removes the source from machine B's list within a few seconds.

## Step: Codec missing

**Action:**
Close TiXL, temporarily rename `Operators/Omt/runtimes/<your platform>/native/libvmx.*` in the installation, and start TiXL with a graph containing `[OmtInput]` and `[OmtOutput]`. Restore the file afterwards.

**Expected:**
- TiXL starts normally.
- Both operators show an error naming the missing `libvmx` file and the platform; nothing else is affected.
