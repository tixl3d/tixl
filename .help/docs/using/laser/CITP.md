# CITP

`Core/Output/CitpLaserHub.cs` + `Core/Output/LaserOutputStream.cs`

Publishes the laser outputs to [Capture](https://capture.se) (or any CITP/CAEX-compatible previz) as laser preview feeds. CITP is the protocol Capture uses to visualize the laser content of the connected console/media server; the CAEX laser extension carries the actual points. See [Laser protocols](Laser%20Protocols.md) for wire details.

This is a **preview/visualization path** - it draws your laser content inside Capture's visualizer, it does not drive projectors.

The transport lives in the Output Setup: add a **CITP capture** under *Add laser device* and bind a laser output to it.

## One source, one feed per laser output

All of the machine's CITP plugs together form **one CITP source named "TiXL"** (one TCP control session per receiver) - the shape MadMapper presents, which is what lets the receiver bind each laser separately:

- the feed list announces every plug as one feed: its resolved **feed name**, listed in feed order;
- the receiver numbers its **"Physical Output"** entries by that feed order, so Capture's laser devices bind feed 1, 2, 3 ... individually;
- every `LaserFeedFrame` datagram carries its feed index, so each feed streams its own output's frames.

A plug is dropped from the list when its output is deleted; the list is re-announced whenever feeds change and every few seconds, so Capture re-binds after either side restarts.

## Device settings (on the plug)

| Setting | Default | Purpose |
|---------|---------|---------|
| `Address` | - | The capture host for a direct connection. Empty = PLoc multicast discovery |
| `Port` | 0 | Direct TCP port. 0 (with an address) or empty address keeps PLoc discovery |
| `Local interface` | any | Interface for discovery and streaming |
| `Feed name` | plug name | Name announced for this output in the source's feed list; empty follows the plug's name |
| `Brightness ceiling` | 1.0 | Final intensity ceiling (0..1) applied to every point right before encoding |
| `Swap color channels` | off | For peers that interpret the packed color as BGR |
| `Keepalive` | on | Keeps this feed streaming while the LASER master is off - Capture keeps showing the show while the real beam is dark |

## Behavior

1. **Discovery** - listens for CITP `PINF`/`PLoc` announcements on multicast `224.0.0.180:4809`. When a peer announces a TCP port, a connection is attempted (announcements from a different subnet are ignored once a local interface is selected). If discovery finds nothing, a direct connection to `127.0.0.1:51245` is tried - that is Capture's default.
2. **Session** - over TCP the source answers `GetLaserFeedList` with all registered feeds, acknowledges `EnterShow`, and re-announces the list every few seconds. Feeds start sending only after the receiver's `LaserFeedControl` asks for them (index + fps).
3. **Streaming** - CAEX `LaserFeedFrame` datagrams over UDP to `224.0.0.180:4809`, each tagged with its feed index: ILDA 16-bit coordinates converted to CITP 12-bit, colors to RGB565, with the feed's brightness ceiling and channel order as the final audit.
4. A frame larger than one datagram (~12,000 points) is truncated with a warning - reduce the point density upstream.

## Usage notes

* Each feed streams after Capture requests it (a start button per feed) and while the **LASER** master is armed.
* If points appear shifted/scaled wrong in Capture, note that TiXL maps the full ILDA range onto Capture's 12-bit space; frame content already sits in that range, so no extra scaling should be needed.
* A rig appears in Capture as one "TiXL (host)" group - like MadMapper's "Virtual Laser 1/2" - instead of one indistinguishable peer per laser.
