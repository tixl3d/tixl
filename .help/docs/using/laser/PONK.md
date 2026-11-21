# PONK

`Core/Output/LaserOutputStream.cs`

Streams laser frames to a PONK-compatible receiver (e.g. MadMapper's Ponk input) via UDP multicast or unicast. PONK (*Pathes Over NetworK*) is the protocol MadMapper uses for laser path streaming; the wire format is documented in [Laser protocols](Laser%20Protocols.md). The transport lives in the Output Setup: add a **PONK endpoint** under *Add laser device*, bind a laser output to it, and the setup streams the routed [LaserCamera] frames.

## Device settings (on the plug)

| Setting | Default | Purpose |
|---------|---------|---------|
| `Address` | 239.255.10.24 | Destination IP - the default multicast group, or a receiver's unicast IP |
| `Port` | 5583 | Destination UDP port |
| `Local interface` | any | Outgoing interface; matters for multicast routing |
| `Multicast` | on | Multicast group vs. unicast target (how MadMapper discovers PONK senders) |
| `Sender name` | "TiXL" | Name embedded in every packet header |
| `Scan speed (x)` | 1.0 | `MAXSPEED` render hint (scanner-speed multiplier request) |
| `Brightness ceiling` | 1.0 | Final intensity ceiling (0..1) applied to every point right before encoding - not bypassable by upstream patches |
| `Swap color channels` | off | For receivers that interpret the packed color as BGR |
| `Keepalive` | off | Keeps streaming while the LASER master is off - a preview override; receivers can drive real scanners, so the master stays a hard kill while this is off. While it overrides a master that is off, the plug shows a Warning status and the stream logs a warning each pump - the bypass is never silent |

## Behavior

* Each frame is packed in the mandatory `XY_F32_RGB_U8` format (float XY in -1..1, 8-bit RGB), prefixed with the two metadata entries `PATHNUMB` and `MAXSPEED`, then split into chunks of up to 1420 bytes per datagram. All chunks of a frame carry the same frame number and byte-sum checksum.
* Frames are capped at 32000 points (protocol limits: point count is a `ushort`, chunk count a `byte`).
* Sender identity: a 32-byte name (the plug's `Sender name`) plus a random id that is generated once and persisted on the plug - receivers (MadMapper keys its Ponk inputs on the id) recognize the sender across editor restarts.
* **Threading model mirrors [EtherDream](EtherDream.md)**: a connection loop owns the UDP socket (bind + multicast interface/TTL/group options) and a send loop drains a queue of max 5 frames. Send errors are retried up to 20 times before the socket is rebuilt.
* **Watchdog**: no fresh frames for about a second blanks the beam instead of looping a stale lit frame.
* The app-bar **LASER** master button keeps sending the last frame zero-colored, so the beam is off while the geometry keeps scanning (settled blanking).

## Usage notes

* MadMapper listens on the default group/port; for a specific machine over unicast, switch `Multicast` off and set its IP.
* Points are clamped into -1..1 and colors reduced to 8-bit - plan bright, saturated content accordingly.
* UDP is best-effort: frames may be dropped on congested networks. Keep frames compact ([Laser optimization](LaserOptimizer.md) increases point count; sometimes less optimization is better for PONK).
* To receive PONK frames instead of sending them, use the [PONKInput](PONKInput.md) operator.
