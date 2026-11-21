# showNET (UDP)

`Core/Output/ShowNetProtocol.cs`

Streams laser frames to a showNET laser (LaserAnimation Sollinger PHOENIX and compatibles) over UDP (device port fixed at **41855**). The protocol module is a byte-exact port of the vendor reference implementation (`com.unknown.net.shownet`), so the wire format matches what the vendor AdminTool produces: TEA-like header/payload obfuscation, handshake (0x04) → frame-memory configure (0x12) → frames (0x1A) with NOP keepalives, sequence-matched request/response, and either delta-compressed or fixed 7/9-byte point records (generation 1 = 24 MHz, generation 2 = 28 MHz point clock).

## Device settings (on the plug)

| Setting | Default | Purpose |
|---------|---------|---------|
| `Address` | - | The laser's IP address. Type it, or pick a discovered laser from the address field's chevron |
| `Scan rate (pps)` | 30000 | Points per second requested from the laser; sets the frame's ticks-per-point divisor (device clock / scan rate) |
| `Brightness ceiling` | 1.0 | Final intensity ceiling (0..1) applied to every point right before encoding - not bypassable by upstream patches |
| `Swap color channels` | off | For projectors that expect BGR |

The showNET port is fixed at 41855; there is nothing to configure there.

## Connection behavior

* **Discovery**: lasers announce themselves by UDP broadcast; the probe listener binds UDP 41856 (one per machine — the vendor AdminTool uses the same port, so at most one of the two can run discovery at a time port-side, and a failed bind retries every 5 s). Streaming does not stop a laser from answering probes (UDP is sessionless), so the picker keeps listing it.
* **Handshake**: up to 5 attempts at 700 ms each, a fresh packet per attempt (the obfuscation is applied in place, so a reused buffer would corrupt the retry). After the handshake the stream sends the frame-memory configure command once, then streams frames.
* **Send loop**: fresh frames beat repeats; between fresh submissions the last frame is re-sent every ~33 ms with a dark approach lead so the device's frame memory loops cleanly. Idle sessions keep alive with NOP packets every 500 ms.
* **Watchdog**: no fresh frames for about a second blanks the beam (the device keeps looping its memory with the geometry, dark).
* **Master off** = blanked output with the session kept warm: showNET has no stop command, so the stream sends the last frame blanked instead of stopping the device; nothing lit can leave the stream while the master is off.

## Safety states

* The app-bar **LASER** master blanks the output (see [Laser safety](Laser%20Safety.md)).
* Every datagram leads with a dark approach from the scanners' last position — the device replays the datagram's memory image from its start, so the approach always covers the seam.
* The plug's status line shows the live state (probing / handshaking / connected / streaming / watchdog).
