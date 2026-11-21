# Laser projection

TiXL can drive real laser projectors: render geometry into a laser point stream
([LaserCamera]), shape it with the output's optimization and safety settings, and
stream it to the projector over EtherDream (ILDA), PONK, showNET or CITP — all configured in
the [Output Setup](OutputSetup.md).

**Start here:**

- [Laser safety](laser/Laser%20Safety.md) — read before enabling a real output.
- [Output Setup](OutputSetup.md) — laser outputs, devices and the master enable.

## The operators

| Operator | Purpose | Doc |
|---|---|---|
| **LaserCamera** | Camera + scene capture: frames any geometry into the laser's viewport | [doc](laser/LaserCamera.md) |
| **MeshToLaserPoints** | Bakes triangle meshes into laser point streams | [doc](laser/MeshToLaserPoints.md) |
| **ImageToLaserPoints** | Vectorizes a texture into laser shapes (Find Paths / Find Contours) | [doc](../operators/lib/laser/convert/ImageToLaserPoints.md) |
| **LaserPointsToBuffer** | Bridges laser frames back into the graph (preview, re-projection) | [doc](../operators/lib/laser/convert/LaserPointsToBuffer.md) |
| **PONKInput** | Receives PONK laser frames over UDP | [doc](laser/PONKInput.md) |

Placement, optimization, safety and the transports are not operators: they are
edited on the laser output and its plug in the Output Setup.

## The transports (Output Setup)

| Transport | Purpose | Doc |
|---|---|---|
| **EtherDream DAC** | ILDA over TCP — real show lasers | [doc](laser/EtherDream.md) |
| **PONK endpoint** | PONK over UDP — e.g. MadMapper | [doc](laser/PONK.md) |
| **CITP capture** | CITP/CAEX preview feed — e.g. Capture | [doc](laser/CITP.md) |
| **showNET laser** | showNET over UDP — LaserAnimation Sollinger PHOENIX | [doc](laser/ShowNet.md) |

The safety pipeline (calibration gate, safe zone, dwell/jump/brightness limits)
and the optimization pipeline (path reorder, blanking, corner anchors) are edited
on the output: [Laser optimization](laser/LaserOptimizer.md),
[Laser safety](laser/Laser%20Safety.md).

## Platforms

The whole laser stack follows TiXL onto Windows, macOS and Linux. Scene capture
reads GPU buffers through TiXL's graphics layer, so it works on both the D3D11
and the Vulkan backend, and the three transports (EtherDream TCP, PONK and CITP
multicast/UDP) are plain sockets — no platform-specific code is involved. The
laser master button, test screen and all Output Setup editing are identical on
every platform; only the graphics backend differs under the hood.

## Robustness

The laser stack is built so a show keeps running and the editor never dies on it:

* **The driver pump cannot take the editor down.** It runs inside the frame loop
  but every stage is isolated — a throwing camera evaluation (broken project
  graph) costs that interval's frame and logs a throttled warning, nothing else.
* **Streams own their threads.** Connection/send loops run on background tasks
  with bounded waits, backoff ladders and in-place recovery; the editor never
  blocks on a device, and every teardown happens off-thread.
* **Watchdogs everywhere.** A 1 s frame watchdog blanks the beam when fresh
  frames stop; the EtherDream transport detects a wedged DAC (stale full buffer)
  and says so instead of silently retrying; CITP re-announces its feed and
  rebuilds dead sessions; PONKInput restarts a dead listener thread on its own.
* **Settled blanking.** Master off never leaves a lit frame on the wall — the
  last geometry keeps scanning with zeroed colors, so scanners stay settled.
* **Thread-safe by construction.** The camera registry publishes immutable
  snapshots, discovery listeners isolate failing subscribers, and the driver's
  state is safe to read from the debug server while the pump writes.

## Background

- [Laser data model](laser/Laser%20Data%20Model.md) — how laser points flow
  through the setup.
- [Points vs. Lines](laser/Points%20vs.%20Lines.md) — what the difference in
  the beam looks like and when to use which.
- [Laser protocols](laser/Laser%20Protocols.md) — EtherDream / ILDA, PONK and
  CITP compared.
- [LaserCore EtherDream library](laser/LaserCore%20EtherDream%20Library.md) —
  the DAC transport implementation in Core.

## Running a show

- [Testing](laser/Testing.md) — bench-testing the chain before a venue.
- [Troubleshooting](laser/Troubleshooting.md) — no output, jitter, dropped
  frames and other common issues.
