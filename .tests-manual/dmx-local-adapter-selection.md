---
id: dmx-local-adapter-selection
title: DMX Local Address Fallback
scope: io-dmx
tags: [dev, essential, edge]
added: 2026-11-11
added-in-version: 4.4
prerequisites:
  - An ArtnetOutput and SacnOutput with channel values connected and SendTrigger disabled.
  - An isolated test network with UDP receivers; do not send test values to live fixtures.
related-help:
  - ../.help/docs/operators/lib/io/dmx/ArtnetOutput.md
  - ../.help/docs/operators/lib/io/dmx/SacnOutput.md
---

Repeat for both operators.

## Step: Preserve the saved address

**Action:** Use a valid IPv4 address absent from this machine. Save and reopen.

**Expected:** The saved value is unchanged and the dropdown shows the saved address with `(not found, bound 0.0.0.0)`. Empty, malformed, IPv6, and multicast source addresses still block output.

## Step: Send through Auto

**Action:** Enable **Send Trigger**. Receive unicast on UDP `6454` (Art-Net) or `5568` (sACN), then test Art-Net broadcast or sACN multicast in the isolated network.

**Expected:** Receivers get the configured universe and channel values. Status reads “Sending. Bound to 0.0.0.0.” with no warning icon; the dropdown annotation explains the fallback. Disable **Send Trigger**; status reads “Sending disabled.” and the dropdown keeps the annotation.

## Step: Select and reconnect

**Action:** Pick Auto, then an available local IP. Save and reopen. Press **Reconnect** after changing address availability.

**Expected:** Auto saves `0.0.0.0`; explicit selections use the selected address. Reconnect refreshes availability. Opening the dropdown alone does not change the active binding. Socket failures remain errors. Disable **Send Trigger** afterward.
