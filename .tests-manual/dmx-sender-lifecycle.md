---
id: dmx-sender-lifecycle
title: DMX Sender Restart
scope: dmx
tags: [dev, edge]
added: 2026-11-18
added-in-version: 4.4
prerequisites:
  - A project contains ArtnetOutput and SacnOutput with changing DMX input and a reachable UDP receiver.
  - A packet capture is available for UDP ports 6454 and 5568.
---

Repeat each step for both output operators with discovery disabled and valid universe settings.

## Step: Stop during frame pacing

**Action:**
Set Max Fps to 1, enable Send Trigger, then disable it while the next frame is waiting. Observe the packet capture for two seconds.

**Expected:**
- Sending stops without waiting for the full frame interval.
- No further data or sACN synchronization packets are emitted after the sender has stopped.

## Step: Restart repeatedly

**Action:**
Toggle Send Trigger off and on repeatedly, then leave it enabled. Change the DMX input and observe the receiver.

**Expected:**
- The receiver resumes receiving the changed values.
- The editor remains responsive and does not report sender exceptions.

## Step: Recover after a delayed sender exits

**Action:**
Using a debugger, suspend the sender thread after it dequeues a frame. Disable Send Trigger and allow the 500 ms stop timeout to elapse, then re-enable Send Trigger. Resume the old sender and continue evaluating the enabled operator.

**Expected:**
- No replacement sender runs while the old sender remains alive.
- After the cancelled sender exits, a new sender starts without another trigger toggle and output resumes.
