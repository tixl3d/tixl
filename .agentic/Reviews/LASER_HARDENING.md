# Laser stack hardening — review, fixes and verification

Source reviewed: `D:\DEVDEVDEV\tixl-multi-allin` (the most advanced laser stack: EtherDream + PONK + CITP
transports, optimizer/safety pipeline, Output Setup driver, LaserCamera/MeshToLaserPoints/PONKInput
operators, LaserPointsToBuffer, preview and discovery).

The reviewed code was taken from the target working tree, including its uncommitted operator improvements,
rebuilt on a clean branch (`fix/laser-hardening`, three hardening passes) and consolidated onto
`feat/laser-support` in `.worktrees/laser-support`.

## What was fixed

### Pass 1 — transports and lifecycle (LaserOutputStream)

- `Dispose()` is synchronous and idempotent. The old fire-and-forget teardown could let a rebuilt stream
  connect to the same DAC while the old session was still closing.
- EtherDream send waits with `await WaitAsync(...)` instead of blocking a pool thread in `Task.Wait`; timed-out
  sends kill the socket and recycle the connection.
- DAC teardown is bounded: stop/prepare run on a bounded task, then the socket is killed, so a wedged library
  socket lock cannot leak a pool thread forever.
- The private `Dac._socket` reflection is cached and logged once if unavailable.
- Stale DAC event callbacks are guarded by the current-session identity, so an old connection cannot blank the
  new show.
- Playback timestamps are `long` ticks updated with `Interlocked`, no more tearing `DateTime`.
- Status message + level published as one immutable snapshot.
- Frame/queue/producer buffers are copied exactly by `Count`; no blanked-capacity tail is streamed.
- `SubmitFrame` is serialized and reuses its safety scratch/output buffer instead of allocating
  `List.ToArray()` per frame.
- PONK retries are bounded and treat a saturated UDP send buffer as a dropped frame instead of stalling the
  send loop.
- CITP control traffic has `SendTimeout`, keep-alive, a peer-activity timestamp and a 15 s stale-session
  check; the old `TcpClient.Connected` bit could stay true forever on a vanished peer.
- CITP receive buffering grows for legitimate large messages and skips oversized messages by their declared
  size without losing the next message boundary.
- CITP `SendLoop` has an outer exception guard (a manually created thread must never terminate the process)
  and receives its cancellation token explicitly.
- CITP CTS disposal is owned by the reconnect task; `_cts` is cleared under the connection lock.

### Pass 2 — production-readiness review (2026-11-08)

### Fixed (most severe first)

1. **Blanking gate was not honoured end to end.** `I <= 0` means "dark" to every safety stage, so such points
   skipped the brightness cap, dwell limit and bounds audit — but no encoder used `I` as a gate: PONK and CITP
   have no intensity channel and the EtherDream encode sent RGB regardless (many projectors ignore `I`).
   A point with `I = 0` and colour was drawn lit, unchecked. Now `LaserSafetyTransforms.NormalizeBlanking`
   runs first in every `Apply`, and all three encoders zero colour for `I <= 0` at encoding as defence in
   depth.
2. **Static-dwell protection was defeated by frame looping.** The DAC replays the last frame, so a frame of
   N < `MaxStaticPoints` points at one spot was an indefinite static beam. `LimitStaticDwell` now treats the
   frame as a loop: runs continue over the seam, only movement ends a run (blanking in place does not), and a
   frame that never moves has all its lit points blanked.
3. **Device E-stop could be cleared automatically.** The halted-engine recovery escalated to
   `Stop + ClearEStop + Prepare`, releasing a hardware/network E-stop without operator action. Recovery no
   longer clears E-stop. A device reporting E-stop now latches and disarms the global LASER master; re-arming
   the master is the only place `ClearEStop` is sent (if the cause persists, the master drops again).
4. **E-stop / fault log flood.** The library raises `SafetyFaultDetected` on every DAC response (every ~4 ms
   poll) while a flag is set; each one was a `Log.Warning`. Now logged once per reason per minute.
   (Rate-limited in `LaserOutputStream`.)
5. **Muting an output, or content going empty, left the last lit frame up for ~1 s** (until the stream
   watchdog). The driver now submits a dark frame (`LaserOutputStream.SubmitBlank`) immediately, also for a
   muted output that was deferred by the pump budget.
6. **Invalid frame-warp quad failed open.** A bow-tie/degenerate corner set returned the unwarped frame —
   i.e. the full field while the user aimed at a small screen. It now returns no frame (dark).
7. **PONKInput: non-finite coordinates from the wire** became `int.MinValue` and, after the safety clamp,
   a lit point in the field corner. They are now blanked points at the centre.
8. **Show brightness ceiling ignored the I channel** (single-colour projectors driven from `I`). It now scales
   `I` too; a lit point whose intensity rounds to 0 becomes a canonical blank.
9. **PONK keepalive override was silent.** With the master off and Keepalive on, live content keeps streaming
   (documented, deliberate). The stream now logs a warning and shows a Warning status while that is happening.
10. **PONK frame number** did not advance for a frame dropped mid-send, so its leftover chunks could share a
    number with the next frame. Now always advances.
11. **Pipeline status string built per output per pump and thrown away.** `Session.Apply` has a status-free
    overload (used by the driver); `DescribeLastRun()` builds the line on request.

#### Behaviour changes to be aware of

- A frame consisting only of a static spot (e.g. a single "beam" dot) is now dark. Beam effects need motion,
  or a dwell run below `MaxStaticPoints` within a frame that moves elsewhere.
- A device E-stop disarms the LASER master for all outputs.
- Muted outputs stream a dark frame instead of nothing (connection stays warm, watchdog stays quiet).

### Pass 3 — optimizer and LaserCamera (2026-11-08)

Full read of `LaserOptimizerTransforms` and `LaserCamera`, which the second pass only spot-checked.

#### Fixed

1. **Corner dwell was inverted.** `CalculateAngle` returned the *turn* angle (0° = straight) while the
   threshold is documented as an *interior* angle ("angles below this count as sharp corners, 180° = straight").
   Every straight sample was repeated `CornerAnchors` times (a 20-point line came out as 56 points: beaded
   lines, a third of the frame rate) and real corners got no dwell. Now the interior angle
   (`CalculateInteriorAngle`). At the default 30° only spikes dwell; a square's 90° corners need a threshold
   above 90° (e.g. 120–135°) to get anchors.
2. **Reorder off dotted every line.** The non-reordering path inserted blanked travel before *every* point, so
   consecutive lit points got blanked samples between them. Dark travel now only precedes a shape start.
3. **Quadratic tour on the render thread.** A concentrated frame (too dense for the tour grid) fell back to an
   O(n²) sequential tour with no upper bound. Above 4096 segments it keeps the original order (still blanked
   and corner-dwelt) instead of stalling the editor.
4. **LaserCamera readback sliced last frame's GPU copy with this frame's element count.** When a scene's drawn
   count grew, the extra elements came from stale buffer contents — one frame of phantom points. Each staging
   copy now records its own slice.

### Documentation updates (this branch)

User-facing behaviour changes documented in `.help/docs/using/laser/`:

- `Laser Safety.md` — static-dwell loop behaviour (single static spot renders dark), brightness ceiling caps
  `I` too, device E-stop disarms the LASER master, muting submits a dark frame immediately, blanking gate
  honoured end to end.
- `LaserOptimizer.md` — corner dwell measures the interior angle (with the 30°/90° guidance), reorder-off
  inserts dark travel at shape starts only, dense-frame fallback keeps input order (no render-thread stall).
- `PONK.md` — keepalive bypass now shows a Warning status and logs; never silent.
- `Troubleshooting.md` — "single static dot renders dark" row.

## Fourth pass — post-port review (2026-11-08, feat/laser-support)

Full independent review of the pass-2/3 delta plus its pass-1 context (thread safety, memory safety,
stability, safety semantics, test quality). The categories thread safety, memory safety, stability and
safety logic were assessed **clean**; two minor races found and fixed on this branch:

1. **PONKInput.SumChunkBytes read `frame.Chunks` without the frame lock.** Writes happen under
   `lock (frame)` on the listener thread while cleanup/counter paths enumerate on evaluator threads — a
   late chunk into an already-removed buffer could throw mid-enumeration (blanking one frame) and
   permanently over-count `_pendingBytes` (starving PONKInput until restart). `SumChunkBytes` now
   enumerates under `lock (frame)`; no call site holds the lock, so no nesting/deadlock.
2. **PonkStream `_encodeBuffer` was an instance field shared by briefly-overlapping send loops.** TearDown
   does not join the old send loop before a new connection starts, so a reconnect could interleave two
   loops' encodes into one datagram (torn frame, within safety limits but a visible artifact). The header
   and encode buffers are now locals of `SendLoopAsync`, owned per connection attempt; `EncodeFrame` takes
   `ref byte[]` so growth propagates back to the loop.

Verification after the fixes (Windows, .NET SDK 10.0.201): Core.Tests 0 errors; laser/PONK/EtherDream
filter **107 passed, 0 failed**; `Lib` and `Editor` 0 errors 0 warnings.

Review observations deliberately left alone: EtherDream per-chunk allocation (GC pressure only, already
documented in pass 3), and the PONK Keepalive master bypass — documented above.

## Fifth pass — EtherDream hardware smoke test (2026-11-08)

Drove the shipped stack against a real DAC (EtherDream-protocol device at 10.0.0.90, clone-style firmware
that answers `?`/`p` on TCP 7765) via a standalone harness (`LaserOutputStream.Create` + blanked frames at
30 Hz, no light by construction: content I=0, door `BeamEnable=false`, 5% ceilings at the door and device).
Harness: `D:\TiXLDEV\tmp-etherdream-smoke\EtherDreamSmoke`.

All phases pass: connect 216 ms; master-off halts playback with the connection warm; armed dark stream at
~24–30 Hz holds 'Streaming' for 6 s; the frame watchdog blanks ~1 s after the last fresh frame and its
Warning persists; resume clears it; dispose is bounded (2–63 ms).

**Found and fixed on hardware:** the send loop's completed-frame status reset ("Streaming", Success)
overwrote the watchdog's Warning within milliseconds — the UI read 'Streaming' while the output was
watchdog-blanked. The reset is now gated on `!_watchdogBlanked` (EtherDream) and PONK gained the mirror
reset gated on `!_watchdogBlanked && !_keepaliveOverrideReported`, so its watchdog/send-error warnings
clear on resume but the keepalive bypass stays visible for the whole session. 107 laser tests pass.

Noted, not fixed: at dispose the stack logs `Dac._socket is unavailable` — the reflection-based socket-kill
escape hatch does not bind against LaserCore.EtherDream.Net 2.0.0 on this firmware; a wedged send
still recovers via the bounded-wait + connection-recycle ladder, but the immediate socket kill is a no-op.
Recorded as a residual risk.

### Analog output verified on a scope (2026-11-09)

The rig is the DAC's ILDA output into an oscilloscope (X-Y mode), and the DAC has a physical arm/enable
that gates the analog stage — until it is armed, the box speaks the whole network protocol (connect,
prepare, begin, data, status playing) while the ILDA outputs stay dead. That is easy to misread as a
software fault; it is device hardware state.

After arming: a breathing/moving square streamed by the harness is clearly visible on the scope with the
correct geometry (both axes, square aspect, centred), confirmed by frame captures from a camera pointed
at the screen (`rtsp://10.0.0.189:8554/rpi_webcam`). Amplitude note: ILDA units map ~1:1 to the DAC's
output, ±32767 = full scale; test patterns at ±1000 units are ~3 % of the field and look tiny on a scope —
full-field patterns (±15000..±30000) are the right bench default. Brightness/colour channels do not
affect the X-Y trace.

## Verification

Windows (this worktree, .NET SDK 10.0.201, Debug):
- `Core.Tests` — builds 0 errors.
- `dotnet test Core.Tests --filter "FullyQualifiedName~Laser|FullyQualifiedName~Ponk|FullyQualifiedName~EtherDream"`
  → **107 passed, 0 failed, 0 skipped** (6 s).
- `Operators/Lib` — 0 errors, 0 warnings.
- `Editor` — 0 errors, 0 warnings.

Linux (`.worktrees/laser-hardening`, .NET SDK 10.0.114, during passes 2–3): 103 and 107 laser tests passed on
the respective passes; `Lib` and `Editor` built cleanly.

## Residual risks (not overclaimed)

- "Unkillable" is not a guarantee against firmware/driver/hardware hangs. The software paths are bounded and
  fail closed, but a wedged third-party DAC library can still require process restart; the socket kill is the
  current escape hatch.
- `LaserOutputStream.CopyForQueue` still allocates an exact-size frame per submission. Pooling is possible but
  needs lifetime tracking for `_lastFrame`/`_sendFrameRaw`; left out to avoid a use-after-return risk.
- `LaserCamera` staging pairs are keyed by capture slot, not by source buffer; a scene that reorders draw
  entries can still reallocate staging buffers. Correctness is preserved; the cost is GPU churn.
- `LaserPointsToBuffer` uploads only the written prefix; consumers must use `PointCount` (documented in the
  output description), not the SRV length.
- CITP activity is application-level; a peer that neither reads nor writes is detected by the 15 s activity
  timeout on the next reconnect check, not instantly.
- `LaserCamera.Finish` allocates an exact-size array per changed frame (`StructuredList` length = array
  length); above ~3.5k points that is a large-object-heap allocation per pump. Needs a `StructuredList` that
  can carry a count smaller than its array.
- `MeshToLaserPoints` with *Update continuously* maps the GPU copy synchronously every evaluation (a
  render-thread stall). It should share `LaserCamera`'s pipelined `StagingPair`.
- PONK Keepalive remains a per-plug bypass of the emergency master (visible since the second pass).
- Frame warp: a degenerate/bow-tie quad now fails closed (dark) instead of streaming the unwarped field.

## Comparison with the other checkouts (2026-11-08)

Checked `D:\DEVDEVDEV` and `D:\TiXLDEV` by git head, per-file hashes and latest laser commits:

- `D:\TiXLDEV\new-multi-tixl` — newer overall (camera-controls line), but its branch contains **no laser
  files**; the laser work lives on the laser branches only.
- `D:\TiXLDEV\tixl-crossplatform` — empty/non-repo; no laser code.
- `D:\DEVDEVDEV\allin-laser-core`, `allin-laser-ops`, `allin-collab`, `allin-media`, `allin-audit`,
  `allin-linux-audit` — all last laser commits 2026-10-25; older than this stack.
- `D:\DEVDEVDEV\tixl` — older 2026-09 architecture (graph-side EtherDreamOutput/PONKOutput/CITPLaser
  operators); its unique fixes (unicast TTL, zero-touch discovery, stable sender/source key, feed-active
  queue gating) are already present in the consolidated stack.
- `D:\DEVDEVDEV\tixl-stable-auto-port` — current branch has no laser files; its laser commit is an ancestor
  of this stack.
- `origin/category/laser` — older divergent branch; `tixl-multi-allin` contains all of its fixes plus the
  later capacity/stale-queue work.
- One genuinely new upstream change applies: the latest resource layer makes `BufferWithViews.Uav` optional
  for draw-only structured buffers. `LaserPointsToBuffer` now requires only the SRV and tolerates a missing
  UAV instead of dropping valid frames.

No newer laser transport/operator code was found than this stack.

## Sixth pass — bench session: PONK, the editor path and two picker bugs (2026-11-09)

### PONK on the wire

The PONK sender was verified on the wire end to end: the harness streamed over multicast
239.255.10.24:5583 and TiXL's own PONKInput received both directions in the editor log
(frames from 'TiXL' and from 'MadMapper-Laser-Output-1' simultaneously). MadMapperDemo listed the
'TiXL' sender and received frames — for about a minute, then lost it.

Cause identified, and it is the bench machine, not the stack: with `Local interface` empty the
send socket binds `IPAddress.Any` and Windows routes multicast through the default route — which
on this machine is the WSL/Hyper-V virtual adapter (172.28.80.1), not the LAN. Delivery to
same-host receivers then depends on route flapping. The fix for real use: set the plug's
`Local interface` to the LAN NIC (10.0.0.69). The full pinned-connection verification in MadMapper
is still pending (blocked by the picker bug below while the session was live).

### Two editor bugs found and fixed during the session

1. **The laser interface picker never refreshed its adapter list** (`LaserPlugDiscovery`): the
   refresh gate seeded the timestamp with `long.MinValue`, so `TickCount64 - long.MinValue`
   overflowed to a huge negative delta and the 10 s background refresh never fired. The picker
   showed only bind-all and localhost forever, hiding every real adapter — which made the
   multicast pin impossible to set from the UI. Fixed (seed `long.MinValue / 2`, first list
   built synchronously). Earlier in the pass the filter was also corrected to accept adapters
   Windows reports as `OperationalStatus.Unknown` instead of `Up` (drivers without media-connect
   state, Hyper-V/WSL NICs).
2. **The hub asserted "2 visible items with conflicting ID"**: the same project folder was
   registered twice (overlapping scan roots; nothing deduped by folder) and the two project cards
   derived their ImGui IDs from the folder. Fixed at three layers: the csproj scan dedupes by
   folder, the load loop and archived-project reactivation refuse an already-registered folder,
   and the hub skips a folder it has drawn.

Also in this session: the EtherDream analog output was verified on an X-Y oscilloscope through the
real editor path (see the scope section above), and the first arm now goes through the laser
safety warning dialog.

### Still open

- PONK: the MadMapper connection held over the pinned LAN interface needs one confirming pass
  (the UI could not express the pin while the session was live).
- CITP remains untested against a live peer.
- `Dac._socket` reflection escape hatch unavailable on LaserCore.EtherDream.Net 2.0.0 (logged at
  dispose; bounded-recovery ladder covers wedged sends).
- Perf leftovers accepted: LaserCamera LOH allocation above ~3.5k points, MeshToLaserPoints
  continuous-update synchronous readback, EtherDream per-chunk allocation.
