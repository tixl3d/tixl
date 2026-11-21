# Troubleshooting

Symptom -> cause -> fix for the laser pipeline. Referenced: [LaserCamera](LaserCamera.md), [Laser optimization](LaserOptimizer.md), [EtherDream](EtherDream.md), [PONK](PONK.md), [PONKInput](PONKInput.md), [CITP](CITP.md).

## Nothing comes out of the projector

| Check | Fix |
|-------|-----|
| Output not sending | `Send` on for the laser output, and the app-bar **LASER** master armed (it re-arms off at every editor start) |
| Status says "Connecting..." forever | Wrong DAC IP - clear the address to auto-pick a discovered DAC, or pick from the address field's chevron |
| Status: "DAC wedged (buffer full, idle)" | A previous session died mid-write and the DAC keeps the stale buffer; power-cycle the DAC |
| Status shows warm-up retries | The projector light engine is still warming; wait (~60 s max), then check the projector itself |
| E-stop warning | Clear the stop on the hardware - the stream's recovery ladder re-arms the session on its own |
| Points counted but nothing drawn | Frame may be all blanked points (`I = 0`) - check upstream of the output |
| No points after [LaserCamera](LaserCamera.md) | Content behind the camera or outside the frustum is culled - aim the camera at the points |
| Preview shows a frame, device gets nothing | Output not bound to a plug, or bound while `Send` is off - check the output card's bound-device list |

## Image looks wrong

| Symptom | Cause | Fix |
|---------|-------|-----|
| Doubled/ghosted shapes, corners smeared | Scanners can't follow | Use [Laser optimization](LaserOptimizer.md); raise `BlankingDelayPoints`/`CornerAnchorCount`, lower the plug's `Scan rate` |
| Shapes drawn in chaotic order, lots of invisible jumps | Path order unoptimized | `EnableOptimization` on [Laser optimization](LaserOptimizer.md) |
| Content smaller than the aimed zone / distorted | Frame mapping | Set the output's *Frame mapping* (Fit keeps aspect, Stretch fills, Crop covers); see the output's safety section |
| Image lands outside the aimed zone | Safe zone vs. content | Check the safe zone and the setup rectangle in the output's safety section |
| A single static dot (beam effect) renders dark | Static-dwell blanking counts the DAC's frame loop | A lone spot that never moves is blanked by design - add motion or more dwell points; see [Laser safety](Laser%20Safety.md) |
| PONK content zoomed/cropped wrongly | Sub-rectangle mapping | Set MinX/MaxX/MinY/MaxY on [PONKInput](PONKInput.md) back to -1..1 |

The streams loop the last frame between the driver's ~30 Hz submissions and blank via the watchdog when fresh frames stop - a paused graph never burns a frozen lit frame and never flickers.

## PONK specific

| Symptom | Cause | Fix |
|---------|-------|-----|
| `FrameCount` stays 0 | Sender/receiver mismatch | Same port (default 5583), same multicast group, `ExpectedSenderId` = 0, firewall allows UDP |
| Receiver loses the sender after a while (or never sees it) while TiXL's own [PONKInput] still receives | Multicast leaves on the wrong network adapter (WSL/Hyper-V virtual adapters can grab the default route) | Set the plug's `Local interface` to your LAN NIC's IP; empty = the OS default route |
| `ExpectedSenderId` filtering drops everything | Id persisted per sender plug | Read the current value from the `SenderId` output, then set it |
| Receiver shows a duplicate sender per restart | Old sessions keyed on old ids | Sender ids persist on the plug now; delete the receiver's accumulated duplicate inputs once |
| Chunks never complete | Fragment loss or timeout too low | Raise `Timeout`; check network for UDP loss |
| Log shows CRC mismatch | Corrupted datagrams, or a sender with the old 48-byte-header bug | Update the sender; see [Laser Protocols](Laser%20Protocols.md) |

## EtherDream specific

| Symptom | Cause | Fix |
|---------|-------|-----|
| Discovery finds nothing | DAC on another subnet/broadcast blocked | Type the DAC's IP directly; check `Local interface` matches the DAC's subnet |
| DAC speaks the protocol (connects, "Streaming") but no analog output | The DAC's own **arm/enable** gates its ILDA stage | Arm the DAC (its physical switch/button) - a disarmed DAC accepts the full network protocol while its outputs stay dead |
| "Discovery port 7654 already in use" warning | Another app owns discovery | Harmless - the shared discovery results are reused |
| Second app can't connect to the DAC | Ether Dream allows one TCP session | Close the other app or unbind this output |
| Send errors repeat, then reconnect loop | Unstable network/DAC, or a wedged DAC (see above) | Check cabling; if the status reports the wedge, power-cycle the DAC |
| Connect / stall / recycle repeats after a crash | The DAC firmware keeps a stale full buffer | Power-cycle the DAC - the stream reconnects on its own |

## CITP / Capture specific

| Symptom | Cause | Fix |
|---------|-------|-----|
| Capture never sees the feed | Discovery never saw Capture's announcement | Check `Local interface` (announcements from other subnets are ignored once one is set); pick Capture from the address dropdown |
| Feed listed but no frames | Capture hasn't started the feed | Click the feed's start button in Capture; the status line then reads "Feed N active at X fps" |
| Feed disappeared after a restart | Control session died silently | The feed list re-announces every 5 s - it comes back on its own; if not, check the above |

## Debugging tools

* The plug's **status line** in the Output Setup is the live view of every transport: connecting, streaming, warm-up, watchdog, wedge detection.
* With the editor started via `--debug-server <port>`, the `getLaserStatus` command reports master/driver state and per-output stream diagnostics over TCP - the remote view during a show.
* `PrintToLog` on the operators - details in the log (open with **F12** in TiXL).
* [Testing](Testing.md) describes the protocol regression harnesses in `Core.Tests` - useful as executable protocol references.
