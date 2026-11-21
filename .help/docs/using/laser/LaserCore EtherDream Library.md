# LaserCore EtherDream Library

[EtherDream](EtherDream.md) is built on the NuGet package **`LaserCore.EtherDream.Net` 2.0.0** (referenced in `Core/Core.csproj`). This page documents the library behaviors the operator code (and its tests) rely on - several of them are surprising and were verified by decompiling the package and driving it against a fake DAC (`Core.Tests/EtherDreamDacProtocolTests.cs`).

## `Dac`

* The constructor performs a **blocking synchronous TCP connect** to port **7765** (hardcoded - the operator's `Port` input is decorative) and starts a 1 s heartbeat timer. **Never construct it on the render thread**; [EtherDream](EtherDream.md) runs it in a background task.
* The heartbeat **auto-reconnects** a dropped connection by itself (recreates the socket, re-fires `DeviceConnected`). Callers only need to retry `StreamPoints` through transient errors - don't tear everything down on the first failure.
* `StreamPoints` **blocks** while pacing points into the DAC's buffer (ping-loop while `NackBufferFull`). Call it from a dedicated send task.
* `StreamPoints` throws `InvalidOperationException` while the light engine state is not `Ready` (projector warm-up). Treat it as *wait*, not *error* - readiness is checked at `StreamPoints` entry against the last heartbeat status.
* `ClearEStop` clears an active emergency stop (the device accepts 0x00 or 0xFF on the 'estop' command).

## Status / response framing

* Every response is 22 bytes: ack byte (`'a'` = 97 on success), echoed command byte, then a 20-byte status. Nack ack codes: `'F'` (buffer full), `'I'` (invalid), `'!'` (stop).
* `DacStatusDto`: Protocol, LightEngineState, PlayBackState, Source, LightEngineFlags (u16), PlaybackFlags (u16), SourceFlags (u16), BufferFullness (u16), PointRate (u32), PointCount (u32). The operator surfaces `BufferFullness` and derives its "playing" state from `PlayBackEngineState`.
* `BeginCommandDto` marshals to **7 bytes** (cmd + uint16 low water mark + uint32 point rate), matching the official protocol (etherdream.com/protocol.html; the doc's `'q' = 0x74` is a typo, it is `0x71`).
* The DAC sends an unsolicited 22-byte hello on connect; a second TCP connection is **rejected** while a session is active (single-host exclusivity).

## `DeviceDiscovery`

* Binds UDP **7654** - only one instance per machine; a second construction throws `AddressAlreadyInUse`. [EtherDream](EtherDream.md) catches this and reuses the shared results.
* `DiscoveredDevices` is a **static shared dictionary that only ever accumulates** (entries are never removed).
* Discovery broadcast (36 bytes, sent once per second by DACs): MacAddress[6] + HwVersion (u16) + SwVersion (u16) + BufferCapacity (u16) + MaxPointRate (u32) + DacStatus (20). Only the sender's *address* is used, so test broadcasters can send from any ephemeral port.
* `GetDeviceName` derives a display name from the broadcast data.

## Point data

* Points are 18 bytes packed little-endian: control (u16), X (i16), Y (i16), R/G/B/I (u16 each), U1/U2 (u16 user values, sent as 0). The operator maps `LaserPoint` onto `DacPointDto` with control = 0; blanking is carried by intensity 0.
* `MaxPointRate` from discovery is used to clamp requested scan rates.

## Test notes

`Core.Tests/EtherDreamDacProtocolTests.cs` contains a protocol-accurate fake DAC exercising the full command set (prepare, begin, point data, status, ping, estop, clear, discovery). See [Testing](Testing.md) for how to run it and one caveat about port collisions with a running editor.
