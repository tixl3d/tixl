# PONKInput

*in [Lib.laser.input](README.md)*

Receives laser frames from any PONK-compliant sender (e.g. MadMapper) over UDP.

A background listener binds to the configured Port (default 5583) and joins the default multicast group `239.255.10.24`; unicast packets to the same port are also received.

## Key Features

**Flexible Input Sources:**
- Accepts frames from any PONK-compliant sender
- Supports multicast group joining AND unicast reception
- Configurable sender filtering (by sender ID or name)
- Automatic frame reassembly from chunked UDP sends
- CRC validation for frame integrity
- Supports both mandatory and optional data formats

**Frame Processing:**
- Reassembles fragmented frames (chunked delivery)
- Validates frame checksums
- Decodes both mandatory formats (XY_F32_RGB_U8 and XY_U16_RGB)
- Converts PONK coordinates to ILDA 16-bit range
- Applies configurable zoom/range restrictions
- Automatically drops incomplete frames on timeout

**Status Monitoring:**
- Tracks frames received, points processed, invalid packets
- Reports listener status via outputs
- Provides per-sender stream tracking for filtering

## Inputs

| Name | Description |
|---|---|
| **Active** (Boolean) | Starts/stops the UDP listener; auto-restarts on socket error |
| **Port** (Int32) | UDP port to listen on (default 5583) |
| **PrintToLog** (Boolean) | Enable verbose bind/join/CRC/parse logging |
| **Timeout** (Float) | Seconds after which incomplete frames are discarded (default 1.2) |
| **ExpectedSenderId** (Int32) | Only accept frames from this sender ID (0 = any) |
| **FilterSenderName** (String) | Only accept frames from this sender name |
| **MinX** (Float) | Left edge of sub-rectangle zoom (PONK coords -1..1; default -1 = full width) |
| **MaxX** (Float) | Right edge of displayed X range |
| **MinY** (Float) | Bottom edge of displayed Y range (default -1 = full height) |
| **MaxY** (Float) | Top edge of displayed Y range (default 1 = full height) |

## Outputs

| Name | Type | Description |
|---|---|---|
| **LaserPoints** (StructuredList) | | Current laser frame as ILDA-style 16-bit points |
| **StatusMessage** (String) | | Current listener status |
| **FrameCount** (Int32) | | Total frames received |
| **SenderName** (String) | | Last sender name that sent a valid frame |
| **SenderId** (Int32) | | Last sender ID that sent a valid frame |

## Notes

- **Multi-format support:** Accepts both XY_F32_RGB_U8 (mandatory) and XY_U16_RGB (optional) formats
- **16-bit conversion:** PONK XY coordinates [-1..1] map to ILDA coordinates [-32767..32767]
- **Color scaling:** 8-bit color values converted to 16-bit (<< 8)
- **Zoom control:** Min/Max X/Y define sub-rectangle zoom window

**See also:** [LaserCamera](../camera/LaserCamera.md) and the laser outputs in the [Output Setup](../../../../using/OutputSetup.md)

---
*Generated from operator definition in Lib.laser.input*