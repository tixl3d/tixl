---
id: artnet-universe-bounds
title: Art-Net Universe Bounds
scope: dmx
tags: [dev, edge]
added: 2026-11-18
added-in-version: 4.4
prerequisites:
  - An Art-Net UDP packet receiver is listening on the configured target address and port.
  - An ArtnetOutput operator has SendTrigger enabled and Sync disabled.
---

## Step: Upper boundary

**Action:** Send 1024 channels of value 11 from input index 0 at universe 32767, and value 22 from input index 1 at universe 0.

**Expected:** Universe 32767 receives only the first 512 channels of value 11; universe 0 receives only value 22. The operator warns and logs the invalid range.

## Step: Invalid starts

**Action:** Change input index 0's starting universe to -1, then 32768.

**Expected:** Input index 0 sends nothing; input index 1 keeps sending value 22 at universe 0. The operator remains in warning status.

## Step: Warning recovery

**Action:** Leave addressing invalid for several seconds, then change to another invalid address. Fix all starts and spans, then make one invalid again.

**Expected:** No additional log warnings until recovery. Valid addressing clears the status warning; the next invalid configuration logs exactly one new warning.
