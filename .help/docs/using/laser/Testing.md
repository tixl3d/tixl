# Testing

The laser protocol layers are pinned by regression tests in `Core.Tests`. They exist because both protocols have bit-level details that are easy to get silently wrong (and that *were* wrong at least once - see the 52-vs-48-byte PONK header note in [Laser Protocols](Laser%20Protocols.md)).

Run them with:

```
dotnet test Core.Tests
```

## `EtherDreamDacProtocolTests.cs`

One large scenario fact (`FullDacProtocolScenario`) running a **fake DAC**: a TCP server that speaks the Ether Dream protocol on 127.0.0.1:7765 plus a fake discovery broadcaster on UDP 7654. It covers:

* the full command set (prepare/begin, point data, status, ping, emergency stop and clear),
* ping-pacing behavior when the DAC reports `NackBufferFull`,
* re-prepare after `NackInvalid`,
* `InvalidOperationException` while the light engine warms up (and recovery once ready),
* safety/light-engine flag handling,
* UDP discovery broadcast parsing (36-byte layout, device naming),
* max point rate clamping,
* kill-and-restart of the connection (library auto-reconnect).

See [LaserCore EtherDream Library](LaserCore%20EtherDream%20Library.md) for the library behaviors these tests pin down.

## `PonkUdpWireFormatTests.cs`

Four facts over real UDP sockets covering the [PONK](PONK.md) sender against the wire spec:

| Test | Verifies |
|------|----------|
| `SingleChunkDatagram_MatchesSpecOffsets` | Exact header layout (magic, version, sender id/name, frame/chunk numbers, CRC at offsets 48-51) and 11-byte `XY_F32_RGB_U8` points |
| `MultiChunkFrame_ReassemblesAcrossRealUdp` | Frames larger than 1420 bytes split into chunks that reassemble in order |
| `ExactMultipleFrame_WithEmptyTrailingChunk_Reassembles` | The canonical empty trailing chunk when frame data is an exact multiple of the chunk size |
| `CorruptedFrame_FailsChecksum` | Byte-sum CRC catches corrupted datagrams |

## `LaserSafetyTransformTests.cs`

Sixteen facts over the point-data safety transforms (`Core/DataTypes/LaserSafetyTransforms.cs`) used by [Laser safety](Laser%20Safety.md) and the built-in output safety in [EtherDream](EtherDream.md)/[PONK](PONK.md):

* safe-zone clamping (into the zone, into the physical field, color preserved),
* jump subdivision (lit jumps carry the destination color, blanked jumps insert blanked points, short jumps return untouched),
* static-dwell blanking (cap respected, run resets on movement, blanked runs ignored),
* intensity ceiling (scaling, blanking gate preserved, stray out-of-range values folded in),
* out-of-bounds audit,
* full-pipeline behavior (BeamEnable kill switch, all stages in one pass, clean frames report `OK`, `BlankCopyInto` for E-stop/watchdog blanking).

These are pure in-memory tests - no sockets, safe to run anytime.

## Caveats

* The EtherDream tests **bind TCP 7765 and UDP 7654** - close a running TiXL editor that has EtherDream discovery enabled first, or the ports collide.
* PONK tests bind ephemeral localhost UDP ports; they are safe to run alongside the editor.
