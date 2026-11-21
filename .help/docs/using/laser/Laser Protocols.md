# Laser Protocols

Wire-level notes for the three protocols implemented in `Operators/Lib/Symbols/laser/`. All of this is covered by regression tests in `Core.Tests/`.

## Ether Dream (TCP)

*Transport:* the DAC listens on TCP **7765**; discovery broadcasts arrive on UDP **7654**.

The implementation uses the `LaserCore.EtherDream.Net` library (see `Dependencies/`). Relevant behaviors learned the hard way:

* The `Dac` constructor connects *blocking* - always construct it off-thread.
* `Begin` sends a 7-byte prepare command; point streaming is paced by the DAC's buffer (`StreamPoints` blocks while feeding).
* The library sends heartbeats (keep-alives) and auto-reconnects on transient drops; warm-up of the light engine surfaces as `InvalidOperationException` until ready.
* An emergency stop must be cleared explicitly (`ClearEStop`); discovery uses a 36-byte UDP broadcast and a shared static device list.
* Point struct: control byte, int16 X/Y, uint16 R/G/B/I plus two unused user values (18 bytes in the packed DAC layout, 8 fields in the library DTO).

See `Core.Tests/EtherDreamDacProtocolTests.cs` for the exact byte-level expectations.

## PONK (UDP)

*Transport:* sender -> group **239.255.10.24**, port **5583** by default; receivers bind that port and join the group. Unicast works too.

Per datagram:

```
offset  size  field
0       8     magic "PONK-UDP" (ASCII)
8       1     protocol version (currently 0)
9       4     sender identifier (int32, little-endian)
13      32    sender name (ASCII, NUL-padded)
45      1     frame number (mod 256)
46      1     chunk count
47      1     chunk number (0-based)
48      4     data CRC (uint32 LE)
52      ...   chunk payload
```

**The header is 52 bytes, not 48** - the CRC lives at offsets 48-51, so payloads start at 52. Maximum datagram size is 1472 bytes, i.e. up to **1420 bytes** of frame data per chunk.

Frame data section (one path per frame):

```
1     data format (0 = XY_U16_RGB, 1 = XY_F32_RGB_U8 - the mandatory one)
1     metadata entry count, then 12-byte entries (8-char key + float32 value)
        - PATHNUMB: path identity across frames
        - MAXSPEED: requested scanner-speed multiplier
2     point count (uint16 LE)
n*11  points: float32 X, float32 Y (each in -1..1), uint8 R, G, B
```

The older `XY_U16_RGB` format packs 5 x uint16 (X, Y, R, G, B).

* The "CRC" is the **sum of all frame-data bytes mod 2^32** - not a polynomial CRC. It is identical in every chunk of a frame so receivers can reject incomplete frames.
* Limits respected by the PONK transport: 32000 points/frame (ushort point count + byte chunk count stay in range).
* Senders may emit an empty trailing chunk when the frame is an exact multiple of the chunk size - receivers must count it to complete the frame.

See `Core.Tests/PonkUdpWireFormatTests.cs`.

## CITP / CAEX laser extension (UDP + TCP)

*Transport:* peer discovery via multicast **224.0.0.180:4809** (CITP `PINF` containing `PLoc` with the peer's TCP port); the session and feed negotiation run over TCP; laser frames are sent as UDP datagrams back to the peer.

* CITP header (20 bytes): `"CITP"` cookie (ASCII), version, message size (uint32 LE), message part/count, content-type cookie. **Cookies are read as ASCII/big-endian constants; integer fields are little-endian.**
* CAEX content codes used (uint32 LE after the CITP header): `GetLaserFeedList` 0x00030100, `LaserFeedList` 0x00030101 (source key + feed count + UCS-2 names), `LaserFeedControl` 0x00030102 (feed index + frame rate), `LaserFeedFrame` 0x00030200 (source key, feed index, frame sequence, point count, then 5-byte points).
* LaserFeedFrame points: X low byte, Y low byte, packed X/Y high nibbles (12-bit coordinates each), uint16 RGB565 color.

See the `CitpStream` constants in `Core/Output/LaserOutputStream.cs`.

## showNET (UDP)

*Transport:* plaintext discovery probe (30-byte broadcast to port **41855**, replies on **41856**); data packets to the device's UDP **41855**.

* Point records: inverted 12-bit X/Y (`(0xFFFF − point) >> 4`), 8-bit colors (generation 1 divides by 512, generation 2 by 256), 7 bytes (gen 1 RGB+intensity) or 9 bytes (gen 2 adds two user-color slots; color-features 5 swaps intensity for three user colors).
* Every packet: 20-byte header (marker flags + crypt/scramble word counts in word 0, response cipher in word 1's high byte, sequence id in word 2), then command fields. Commands: handshake `0x04` (full obfuscation, reply awaited, sizes 124/132/232), frame-memory configure `0x12` (reply ignored), frames `0x1A` (fragment count/sequence/offset/size fields support multi-datagram frames; the 8-byte frame header travels in fragment 0), NOP keepalive (`0x1A` without a frame header).
* Obfuscation: a TEA-like 32-round cipher (delta `0x9E3779B9`, fixed key) over 8-byte blocks, and a 4-word LFSR keystream scramble — one scrambler per packet, running continuously from the header into the payload.
* Identity words in every discovery reply and handshake response arrive XOR'd with `0xD49A3433`.

See `ShowNetProtocol`/`ShowNetFrameEncoder` in `Core/Output/ShowNetProtocol.cs`; characterization tests in `Core.Tests/ShowNetProtocolTests.cs`.
