# EtherDream

`Core/Output/LaserOutputStream.cs`

Streams laser frames to an [Ether Dream](https://etherdream.com) DAC over TCP (fixed port **7765**). This is the primary path for driving real show lasers. The transport lives in the Output Setup: add an **EtherDream DAC** under *Add laser device*, bind a laser output to it, and the setup's [LaserDriver](../../using/OutputSetup.md) streams the routed [LaserCamera] frames to it.

## Device settings (on the plug)

| Setting | Default | Purpose |
|---------|---------|---------|
| `Address` | - | The DAC's IP address. Type it, or pick a discovered device from the address field's chevron |
| `Scan rate (pps)` | 30000 | Points per second requested from the DAC (0..100000). Stream fewer points per second than this and the DAC buffer drains and playback halts |
| `Brightness ceiling` | 1.0 | Final intensity ceiling (0..1) applied to every point right before encoding - not bypassable by upstream patches |
| `Swap color channels` | off | For projectors that expect BGR |

The Ether Dream protocol port is fixed at 7765; there is nothing to configure there.

## Connection behavior

* **Discovery**: DACs announce themselves by UDP broadcast on port 7654. A DAC being streamed to stops broadcasting, so the address picker also offers the currently connected device.
* **Connection loop**: reconnects every 2 s until cancelled and rebuilds the connection after fatal errors. All of this runs on the stream's own threads - the editor never blocks on the DAC.
* **Send loop**: paces points into the DAC buffer at the configured scan rate. Transient errors are retried; light-engine warm-up (`InvalidOperationException`) is retried for up to ~60 s before surfacing as a warning. Every frame - fresh or looped - leads with a dark ramp from wherever the scanners sit (the previous frame's exit, or park after a begin), so playback never commands a single-sample cross-field step.
* **Watchdog**: if no fresh frames arrive for about a second (stalled graph, paused project), the beam blanks instead of freezing the last lit frame.
* The plug's status line shows the live state (connecting / streaming / warm-up / buffer).

## Safety states

* While the projector's light engine warms up, sends fail and are retried quietly.
* The app-bar **LASER** master button **stops the DAC** when disarmed - playback halts and the light source goes hard off (a projector that emits on zero-color points cannot keep drawing), while the TCP session stays warm so re-arming resumes within a moment. See [Laser safety](Laser%20Safety.md).

## Usage notes

* Bind the output, check the frame preview and the status line before pointing hardware at anything. See [Laser safety](Laser%20Safety.md).
* If frames look unstable, the usual culprits are: too few points per frame for the scan rate (check [Laser optimization](LaserOptimizer.md)), a scan rate set higher than the scanners can follow, or frame drops from an overloaded queue.
* Only one consumer can own the discovery port; a second stream reuses the shared discovery results automatically.
