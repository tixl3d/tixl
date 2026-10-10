# Frame Timing, Pacing and Profiling — Research Report

Status: research report, 2026-10-10. Nothing here is implemented yet except the fix noted in §1.
Scope: refresh-rate assumptions, the time step, presentation and vsync, the Vulkan merge, profiling, and
what to expose to users. Grounded in `main` at `b81b15a87` and `origin/feat/linux-port` at `abb0fd313`.

Balance rule for everything below: an item is either **neutral** (benefits D3D11 now and Vulkan later),
**cheap D3D11-only** (worth doing because it is tiny or informs the Vulkan design), or **Vulkan-later**
(designed now, built on the Vulkan backend). `Plan_CrossPlatformV5.md` deletes D3D11 before v5.0, so
DXGI-specific machinery with real cost is not worth building.

---

## 1. What We Measured on 2026-10-10

A bisect with an injected probe (SphereFlower, 10 s per mode, interval = frame start to frame start;
procedure in [`../BISECT_PROTOCOL.md`](../BISECT_PROTOCOL.md)) found:

| Build | vsync on: std / p95 / hitches | vsync off: mean interval |
|---|---|---|
| v4.0.4.4 (2025-07, blit swap chain) | 0.31 ms / 17.3 / 0 | **4.68 ms** (uncapped, ~214 fps) |
| FlipDiscard `933d065a1` (2026-04) | 0.76 / 16.8 / 1 | 8.42 ms |
| last good `737325953` (2026-09-24) | 0.42 / 16.8 / 0 | 8.35 ms |
| **`b92bde57a`** (2026-09-24) | **10.73 / 29.1 / 225** | 8.34 ms |
| main + `MaximumFrameLatency = 1` (`b81b15a87`) | **0.075 / 16.8 / 0** | 8.34 ms |

- **Jitter (fixed).** `b92bde57a` stopped presenting the hidden Viewer swap chain every frame. That extra
  `Present(1)` had masked Main's frame-latency waitable running at `MaximumFrameLatency = 2`, which lets the
  loop run a frame ahead: intervals alternated between ~1.7 ms and ~31 ms. Latency 1 fixed it.
- **"Vsync off" has been capped since the flip model (open, item 2 of the request).** Raw per-frame data
  shows frames in pairs that add up to one refresh period (6.3 + 10.4 ms, 4.2 + 12.5 ms): exactly two frames
  per 16.68 ms vblank. CPU work stayed at 2–3.5 ms throughout. A flip-model swap chain without
  `DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING` is composed by DWM and throttled by buffer availability, so
  `Present(0)` never runs free. The 4.68 → 8.4 ms "regression" is a presentation-model change, not slower
  rendering. **Confirmed 2026-10-10:** the parent `13e9c88eb` measures 4.63 ms (2,162 frames in 10 s, uncapped),
  `933d065a1` 8.42 ms. Next: an `ALLOW_TEARING` experiment on main.

## 2. How Time Advances Today

Editor frame order (`Editor/UiContentDrawing/WindowsUiContentDrawer.cs:110-244`):

1. `Main.WaitForFrameLatency()` — DXGI waitable, latency 1 (`AppWindow.cs:181-211`).
2. `UiContentUpdate.TakeMeasurement()` — ImGui `DeltaTime` from a private Stopwatch; no clamp, no smoothing.
3. `ImGui.NewFrame()`, then `OutputPresentation.UpdatePresentation()` — **evaluates and composites every
   bound output before `Playback.Update`**, so outputs, NDI and Spout see the previous frame's time.
4. `T3Ui.ProcessFrame()` → `Playback.Update()` (`Core/Animation/Playback.cs:97-145`):
   `LastFrameDuration = RunTimeInSecs − _lastFrameStart`. Static, wall clock, sampled mid-frame after
   variable CPU work, unclamped, unaffected by pause and speed.
5. Graph evaluation in the output window, UI draw, `CaptureUiFrame` (full back-buffer copy every frame),
   then `ProgramWindows.Present`: Main, Viewer if shown, every output window, all with the same sync interval.

Consequences:

- **No time step reaches operators.** `EvaluationContext` has `LocalTime` / `LocalFxTime` but no delta.
  Ops either read the global `Playback.LastFrameDuration`, difference `RunTimeInSecs` (non-deterministic in
  export), difference `LocalFxTime`, or step once per evaluation.
- **The visual clock jitters even when presentation is perfect.** With vsync, frames are displayed exactly
  one refresh period apart, but `LastFrameDuration` is measured at an arbitrary point mid-frame, so
  animations advance by 16.2, 17.1, 16.5 … ms. That is the user's item 4.
- **`Playback.FrameSpeedFactor`** is documented as "2 for 120 Hz displays" (`Playback.cs:80-88`) but is only
  set during export (`RenderTiming.cs:126`, `fps/60`) and is 1 everywhere live.
- **Nothing knows the refresh rate.** No `IDXGIOutput`, `QueryDisplayConfig`, `DwmGetCompositionTimingInfo`
  or `GetFrameStatistics` call exists. `FrameTimeGrader` (`Core/Stats/FrameTimeGrader.cs`) can snap to common
  rates but has no callers. The branch's `SdlDisplayProvider` reads SDL's refresh rate but rounds it to an int
  and uses it only to list modes.

## 3. Cluster A — The 60 Hz Assumption

An audit found ~30 hard refresh assumptions, ~45 per-frame damping sites in C#, and ~40 library graph
symbols that integrate once per frame. Grouped by how they fail:

### A1. Literal 60 Hz in timing logic

| Where | Effect off 60 Hz |
|---|---|
| `Core/Audio/SoundtrackClipStream.cs:330-332` — A/V offsets `±2/60`, `−1/60` s | Latency compensation wrong by ~19 ms at 144 Hz |
| `Core/Utils/MathUtils.cs:711-735` — springs clamp dt to `1/60` | Springs in slow motion below 60 fps, **including 24/25/30 fps exports** |
| `Lib/io/audio/DetectBpm.cs:45-185`, `DetectBeatOffset.cs:54-120` | One FFT sample per frame assumed to be 1/60 s: **BPM is wrong** off 60 Hz |
| `Core/Audio/AudioEngine.cs:428`, `SpatialOperatorAudioStream.cs:245` — velocity `*60` | Doppler scales with frame rate |
| `Editor/Gui/Interaction/Variations/BlendActions.cs:244` — `1/60f // Fixme` | Variation blend speed scales with frame rate |
| `Editor/Gui/Windows/Analyze/Metrics.cs:453` — `ExpectedFramerate = 60` | Perf bar scale and the "60 fps mark" |
| `Core/Stats/PerformanceMetrics.cs:17-37` — 400-frame window, 0..32 ms buckets | 6.7 s at 60 Hz but 2.8 s at 144 Hz; everything ≥ 30 fps lands in the last bucket |
| `RenderExportEstimate.cs:70-72` | Uses the vsynced live frame time as render cost: never estimates below one refresh |
| `NdiInput.cs:373` — drops frames if not updated within `1/30` s | All NDI input lost below 30 fps |
| MediaPipe ops — synthetic timestamps `+= 33333` µs (7 ops) | Assume 30 fps input |

### A2. Per-frame damping without dt (~45 C# sites)

Highest impact: `MidiInput` (`Lerp(…, 0.94)` per evaluation, **154 graph instances**), `Damp` / `DampVec2/3` /
`DampAngle` via `MathUtils.LinearDamp` ("TODO: Fix damping factor from framerate", ~40 uses), `Spring*`,
`DampPeakDecay`, `AudioReaction` (via `AudioAnalysisContext.cs:223-290`, whose decay runs once per frame in
soundtrack mode but once per WASAPI buffer in input mode — the same project reacts differently per refresh
rate *and* per audio source), `BeatTiming`'s 10-frame `SlidingAverage` (75 ms lag at 60 Hz, 31 ms at 144 Hz).
Editor: MagGraph `SmoothItemPositions` (0.33), slider overlays, `DopeSheetArea`, `SpaceMouse`,
`CameraInteraction` (linearised `dt*k*60` that saturates at 30 fps; wheel zoom per notch 1.07× at 144 Hz vs
1.33× at 30 Hz).

**Mechanical fix that keeps today's 60 Hz look exactly:** a per-frame lerp toward a target with weight `d`
is equivalent to a time-based factor `d^(dt·60)`. One helper (`MathUtils.DampFactor(perFrameAt60Hz, dt)`)
converts every site without changing behaviour at 60 Hz. Neutral, mechanical, reviewable per file.

### A3. Per-frame GPU simulations

31 library graphs + 13 examples gate on `HasTimeChanged` and step once per frame: `ParticleSystem` and all
16 particle forces, 7 point sims, `PointTrail*`, `SlidingHistory`, `AdvancedFeedback*`, `FluidFeedback`.
Only `ParticleSystem` and `ReconstructiveForce` divide by `FrameSpeedFactor`. `TimeConstBuffer.LastFrameDuration`
reaches shaders but no shader uses it. History buffers (`PointTrail`, `KeepFloatValues`) count frames.

Options, in increasing cost:
1. **Set `FrameSpeedFactor = refresh/60` live.** Fixes the two ops that honour it; the rest keep stepping
   per frame. Cheap, but alone it makes those two inconsistent with the others.
2. **Pass a per-frame dt to shaders** and convert integrators (`pos += v·dt`, `v *= pow(1−drag, dt·60)`).
   Changes the look of existing projects slightly at non-60 rates; at 60 Hz identical.
3. **Fixed 60 Hz simulation tick with an accumulator** (step 0..n times per frame). Bit-identical to today's
   60 Hz look on any display, deterministic in export, but GPU cost scales with substeps and motion judders
   at 144 Hz without interpolation.

**Decided:** (2), time-based integration everywhere, without a legacy 60 Hz mode — a slightly different look
in existing projects at non-60 Hz rates is acceptable. What must stay stable is the **reference step**:
visual reference tests (and export) run with a fixed step independent of the display, so a reference image
series recreated once after the conversion stays valid on every machine. That means generalising export's
fixed-step mode (`IsRenderingToFile` + `RenderTiming`) into a `Playback` fixed-step mode the test runner
uses too.

**Decided — buffers count frames, lifetimes count seconds:**
- Buffer and history lengths (`PointTrail*`, `SlidingHistory`, `KeepFloatValues`, particle buffer size) stay
  in frames and are **never adjusted automatically**. A trail of 100 frames is 1.67 s at 60 Hz and 0.83 s at
  120 Hz; users adapt buffer sizes when they move a project to a higher rate. The `.help/` page on frame
  rates must say so.
- Lifetimes and durations are seconds. Particle lifetime already is (`ParticleSystem.hlsl:134-139`,
  `(Time − BirthTime) / lifeTime`), but emission is per frame, so at higher rates a full ring buffer
  recycles particles before their lifetime ends — the visible cue to enlarge the buffer. The automatic
  lifetime (`LifeTime < 0`) hard-codes 60 Hz (`maxParticleCount / (newPointCount * 60.0)`); it describes how
  long the buffer lasts, so it must use the real frame rate.

### A4. A global frame-timing property (user items 1.1–1.4)

Introduce one place that knows the display cadence, readable from Core (Player and ops need it):

- `RefreshPeriodSec` — measured, not queried: the median interval of vsync-paced frames over the last ~2 s,
  snapped to the OS-reported rate when within 0.5 %. Measurement works on both backends and on every OS;
  the OS query (`QueryDisplayConfig` gives exact rationals like 59.94 = 60000/1001; SDL3's display mode has
  `refresh_rate_numerator/denominator`) is only a prior. Never round to an int — 59.94 vs 60 is one dropped
  or doubled frame every 16.7 s.
- `VisualDeltaSec` — see Cluster B.
- `TargetFramePeriodSec` — refresh period × present interval (§C3).

Then: `Metrics.ExpectedFramerate`, `PerformanceMetrics` windows (seconds instead of frames) and buckets
(relative to the period), `FrameSpeedFactor`, `RenderExportEstimate` all read from it. Neutral.

## 4. Cluster B — The Time Step and the Visual Clock (user item 4)

Agree with the proposal, with a guard against drift. Three clocks, as already sketched in the archived
`Plan_RenderProfiling.md` M3 (never implemented):

| Clock | Use |
|---|---|
| `WallDelta` | Raw Stopwatch delta, sampled once at frame start right after the latency wait (not mid-frame) |
| `VisualDelta` | What animations and playback advance by |
| `AudioDelta` | Derived from the audio stream when audio is master |

`VisualDelta` with vsync on and a known period `T`:

```
k = round(wallDelta / T)                 // whole refresh periods since the last frame
visual = k >= 1 && |wallDelta − k·T| < 0.3·T ? k·T : wallDelta
drift += wallDelta − visual              // keep long-term sync with the wall clock / audio
if |drift| > T/2: visual += drift; drift = 0   // rare, and visible only as one corrected frame
```

Without vsync, `VisualDelta = WallDelta` (optionally median-of-3). When present statistics are available,
replace the estimate with real display timestamps: DXGI `GetFrameStatistics` (`SyncQPCTime`, cheap but
D3D11-only → hold), Vulkan `VK_KHR_present_wait`/`present_id` today and `VK_EXT_present_timing` where drivers
support it (Vulkan-later).

Also fix while touching this: the static `_lastFrameStart` in both `Playback` and `BeatTimingPlayback`
(one-frame spike when switching between them), and the inverted `IsRenderingToFile` setter
(`Playback.cs:61-76` assigns `RunTimeInSecs` when switching *to* file time and `TimeInSecs` when switching
back, so `LastFrameDuration` spikes by roughly the app's runtime at export start and end).

Audio: BASS free-runs; `SoundtrackClipStream` re-seeks when drift exceeds 40 ms. With a stable visual clock
the drift budget can shrink. Express the A/V offsets in seconds measured per device, not "2 frames".

## 5. Cluster C — Presentation and Vsync

### C1. Present modes, named backend-neutrally (user item 2)

| User-facing | Vulkan | DXGI today |
|---|---|---|
| Vsync (smooth, default) | FIFO | `Present(1)` |
| Low latency, no tearing | MAILBOX | `Present(0)` flip model — DWM shows the newest frame; capped at 2 frames per vblank as measured |
| Uncapped (tearing, benchmarking, VRR) | IMMEDIATE | `Present(0, AllowTearing)` with `ALLOW_TEARING` swap-chain flag — **not implemented** |

**`ALLOW_TEARING` experiment (2026-10-10, main `b8a145948`, SphereFlower):**

| Main swap chain | vsync on: std / p95 / hitches | vsync off: mean |
|---|---|---|
| without tearing flag (control) | 0.087 ms / 16.8 / 0 | 8.34 ms (2 per vblank) |
| created with `ALLOW_TEARING` | **9.74 ms / 29.1 / 186** | **4.45 ms** (uncapped) |

Tearing works for vsync off, but the flag on the swap chain alone brings the vsync-on jitter back, even
though vsync-on presents don't pass `PresentFlags.AllowTearing`: `Present(1)` alternates between blocking
~26 ms and ~1.6 ms, so the latency waitable no longer paces the loop. Consequence: create Main's swap chain
with the flag only while vsync is off, and recreate the swap chain when vsync is toggled (`ResizeBuffers`
cannot add or remove the flag). That is no longer a one-line change — it needs swap-chain recreation with
back buffer, render target view, waitable handle and window association — but it mirrors Vulkan, where a
present-mode change also recreates the swapchain. The experiment (support check through
`IDXGIFactory5::CheckFeatureSupport` via the COM vtable, since SharpDX 4.2 has no `Factory5`) is kept outside
the repo as `_bisect/allow-tearing-experiment.patch`.

### C2. Several windows, several displays (user item 1.5)

Today every output window and the Viewer present with the same sync interval right after Main, and only
Main has the waitable. A 59.94 Hz projector next to a 60 Hz monitor, or a 60 Hz projector next to a 144 Hz
editor monitor, will periodically fill a queue and block the whole loop.

Design (neutral): **one pacing display** waits on vsync; every other window presents without blocking.
- Choose the pacing display: the main editor window while no output is bound; **the output display as soon as
  one is bound** (tentative decision — the projector matters more than the UI). With several bound outputs,
  the first binding paces unless the user picks another. Verify with PresentMon that the editor window then
  stays smooth enough at the output's rate.
- DXGI: secondaries `Present(0, DoNotWait)` and skip on `WAS_STILL_DRAWING`. Vulkan: MAILBOX for secondaries
  (already the plan's intent, `Plan_CrossPlatformV5.md:140`, not what the branch does) and acquire with a
  zero timeout. On Wayland, FIFO on an occluded surface can block indefinitely — never let a non-pacing
  window block.
- Rate mismatch is judder, not a bug: content rendered for 60 Hz on a 50 Hz projector drops every sixth
  frame. Output Setup should show each bound display's refresh and warn when bound displays differ.
- Multiple GPUs: a display on another adapter means cross-adapter copies per frame. Out of scope, but
  Output Setup should at least report which adapter drives each display.

### C3. Higher refresh rates (user item 1.6)

At 120/144 Hz, rendering every frame is better when the frame fits; skipping every second vblank is better
than alternating 1/2-period frames when it doesn't. Offer a **target rate**: Display (default), Half display,
or Fixed (e.g. 60). DXGI supports `Present(2)` on the flip model (cheap). Vulkan FIFO has no interval, so
half rate needs `present_wait` (wait for the previous present id, then sleep to the next even vblank) or
present timing — Vulkan-later. An adaptive mode (drop to half after N misses, return with hysteresis) is
what consoles do and avoids the 1-2-1-2 cadence that reads as stutter.

### C4. Other presentation findings on main

- **Present queue fills up after stalls (found 2026-10-10, not fixed).** After a stall (startup loading,
  compiling; reproduced with the bridge's `stallMainThread`, 1 → 2–3 queued, persistent) the main window's
  flip queue stays full. PresentMon, both states "Hardware Composed: Independent Flip": good state 1 frame queued,
  12.9 ms present-to-display, even present cadence; bad state 2–3 queued, 53.3 ms, present intervals std 9.7 ms —
  while the display itself stayed even (std 0.02 ms). So the visible jitter is animation time sampled at uneven
  frame starts (Cluster B) plus ~40 ms extra latency. Minimising and restoring empties the queue.
  The frame-latency waitable does not prevent this: it counts frames the GPU finished, not frames shown.
  Attempts that failed: (1) an extra blocking wait while more than one frame is queued — blocked ~170 ms per frame,
  which triggered the stall overlay in a loop; (2) draining surplus waitable signals without blocking — no effect,
  there is no surplus; (3) waiting for vblanks until one frame is queued — fired on almost every frame, because
  Windows sometimes picks "composed by DWM", where two queued frames are normal, and the statistics occasionally
  report stale counts. Windows presents the editor as a hardware overlay while it is the foreground window
  and composed by DWM otherwise. Attempt (4), the vblank drain only in overlay mode, also failed: in overlay mode
  2 queued can be a steady state without any stall (the drain fired every frame for seconds, the count returned to
  2 after each present). Conclusion: the queued count from frame statistics is a diagnostic, not a control signal.
  Attempt (5), parked 2026-10-10: `ResizeBuffers` to the same size on the first frame after the stall overlay
  presented (`StallWatchdog.NotifyRenderingBackBuffer`). After a 3 s stall the queue read 2 instead of 2–3, but never
  returned to 1 — unclear whether the reset only partly empties the queue or 2 is a second steady state in overlay
  mode (seen before without any stall). To resume: log the reset with the queued count before and after, and record
  PresentMon right after the stall to see whether "2 queued" really means ~30 ms to screen. Untried alternative:
  present the stall overlay without touching Main's flip queue (a separate topmost window).
  **Parked** in favour of the visual clock (Cluster B), which removes the visible animation jitter in any queue state. The stall overlay's own presents (from the watchdog thread, without a frame-latency
  wait) are the likely trigger in overlay mode; Vulkan will need the same care for presents outside the frame loop.
- The Performance window and `getMetrics` show the presentation mode (composed / hardware overlay /
  independent flip) and queued frames; mode changes are logged.

- `CaptureUiFrame` copies the full back buffer every frame for the stall overlay's background (the flip model
  discards the back buffer, and the watchdog only notices a stall after it started, so the copy must exist
  beforehand). A periodic copy (every 100 ms) was tried and rejected: a potential hitch every sixth frame is
  worse than a constant small cost. **Idea for later:** a capture mode that is switched on around operations
  likely to stall — rebuilds, project loading, saving, shader compilation — and switched off again after a
  stable stretch of frames. Outside that mode, no copy (unless the UI is mirrored to the second view).
- `MaximumFrameLatency = 1` serialises CPU and GPU: once CPU + GPU exceed one period, the rate halves instead
  of degrading gracefully. Latency 2 probably only looked bad because of the clock: frames *started* 1.7 ms
  and 31 ms apart while averaging exactly one per refresh, so a 2-deep queue may have displayed them evenly
  while the animation time, sampled at those uneven CPU moments, jumped. With the visual clock of Cluster B
  (advance by refresh periods or presentation timestamps, never by CPU wall time), latency 2 should be smooth
  and faster. Confirm with PresentMon (displayed-time cadence at latency 2) before changing defaults.
- **Latency vs throughput depends on the use case** and becomes a setting (Cluster G):
  - Live audio input (audio-reactive): latency matters; every queued frame adds one period between sound and
    picture, and it cannot be compensated because the input isn't known ahead. Latency 1.
  - Soundtrack playback and beat-locked tapping (VJ sets): the latency is a constant offset that can be
    phase-shifted away — for soundtracks by sampling the audio analysis at the predicted display time instead
    of the CPU time. Throughput wins: latency 2.
- Player: default adapter (`DriverType.Hardware`, skips the editor's discrete-GPU choice), no waitable,
  queue of 3. The Player is where pacing matters most for shows.
- Minimised with "suspend rendering": the loop waits on the waitable, then returns without presenting
  (`WindowsUiContentDrawer.cs:119-131`); after the first iteration the wait likely times out at 1000 ms.
  Inferred from DXGI semantics, unverified.
- All windows are borderless; exclusive fullscreen is never used. Fine for flip model (independent flip
  gives the same benefits) — verify with PresentMon that output windows reach "Hardware: Independent Flip".

## 6. Cluster D — The Vulkan Merge (user item 3)

`origin/feat/linux-port` is 111 commits ahead, 736 files, +38k lines. On Windows, **the editor defaults to
Vulkan** (`Editor/App/ProgramWindows.cs:166-168`, `TIXL_BACKEND=d3d11` to opt out); the Player stays on D3D11
unless `TIXL_BACKEND=vulkan`. WinForms is replaced by SDL3 on both backends.

What changes for frame timing on Windows after the merge:

1. **The latency-1 fix does not carry over to Vulkan.** `WaitForFrameLatency()` is a no-op on Vulkan
   (`VulkanSwapchain.cs:84-86`). Pacing comes from 2 frames in flight + 3 FIFO images, with the blocking
   points (fence wait in `BeginFrame`, image acquire in `PrepareRenderingFrame`) *after* input is read.
   Expect one or two frames more input latency and a real chance of the alternating-interval jitter we just
   fixed. `b81b15a87` also conflicts with the branch's rewrite of `AppWindow.cs`; the branch's D3D11 path
   already uses latency 1 (`Graphics.D3D11/D3D11Swapchain.cs:46-57`).
2. **Every window is FIFO and acquire-blocking**, contrary to the plan's "one swapchain paces". On mixed
   refresh rates the slowest display paces everything.
3. **GPU timing goes dark on both backends.** `Graphics.Compat/DeviceContext.cs:429-446` stubs queries, so
   `GpuMeasure` reports nothing even with `TIXL_BACKEND=d3d11`. No `vkCmdWriteTimestamp` yet.
4. **Metric meanings shift.** `RecordPresent` times only the queued present (≈ 0); the vsync wait moves into
   the "Draw" metric because the acquire happens after `UiRenderingStarted`; `vkQueuePresentKHR` in
   `EndFrame` is not timed.
5. **New hitch sources:** synchronous pipeline creation at first draw (no `VkPipelineCache`), one `slangc`
   process per shader entry point, `SubmitOneShot` with `vkQueueWaitIdle` under the queue lock (a worker
   thread's upload stalls the main thread), and `Map(Read)` → `FlushAndWait()` draining the whole GPU — which
   turns every per-frame readback op into a full sync. Toggling vsync or resizing runs `vkDeviceWaitIdle`.
6. **Hardware video decode and NDI/Spout zero-copy** are unavailable on Vulkan (no native handle /
   `AdoptTexture`), so video falls back to software decode.

**Release constraint (decided): 4.3 ships on D3D11 from main; the linux-port branch merges after 4.3.**
v4.4 is months away. The branch changes the D3D11 path too — SDL3 instead of WinForms, 3 back buffers instead
of 2, the latency wait moved before event polling, GPU queries stubbed (`GpuMeasure` blank), stall overlay
changed — so after the merge, D3D11-through-the-facade must still be measured against the 4.3 baseline with
the probe (SphereFlower + a GPU-heavy scene, vsync on/off), even while Vulkan is the default. Record that
4.3 baseline before the merge.

Recommended merge gate (timing only): run the bisect probe on the branch on Windows for both backends
(SphereFlower plus one GPU-heavy scene) and compare with main. The probe's three anchors exist on the branch
by inspection. Before Vulkan becomes the Windows default: a latency-1 equivalent (`VK_KHR_present_wait` +
`present_id`: wait for the previous present before polling input; fall back to 1 frame in flight), one
pacing swapchain with non-blocking secondaries, timestamp queries implemented in the facade for both
backends, and `Present` timing that includes acquire and queue-present.

Vulkan presentation on Windows itself goes through DWM for windowed swapchains, and drivers differ (NVIDIA
offers "layered on DXGI swapchain" as a present method). FIFO/MAILBOX behaviour must be measured per vendor,
not assumed — PresentMon shows the actual present mode.

## 7. Cluster E — Measurement and Profiling (user item 5)

### What exists

`PerformanceMetrics` (frame, draw, present, GC alloc), `RollingMetric`, the Performance window with
histograms and CSV, `OutputCompositor` µs stats, `RenderStatsCollector` counters, `OpUpdateCounter`, and
`GpuMeasure` (measures every other frame at best). No GPU frame time, no per-op timing, no external profiler
hooks, no D3D annotations. `Plan_OpGraphProfiling.md` (archived) is entirely unimplemented;
`Plan_RenderProfiling.md` (archived) shipped M2 partially and none of M1/M3/M4.

### Bugs found on the way (cheap, neutral)

- `EnableFrameProfiling` is **on by default** (`UserSettings.cs:232`) and adds an interval event plus a
  formatted string to a `DataChannel` every frame that is never trimmed (`Editor/App/Profiling.cs`,
  `DebugDataRecording.cs`): per-frame allocation and unbounded memory growth over a session.
- The frame metric is recorded from the app-menu-bar draw code (`Metrics.cs:72`), so it stops when the menu
  bar is hidden while Draw and Present continue.
- The Performance CSV export omits the Present column.
- `GpuMeasure` alternates measuring and polling.

### Proposed layers

1. **Always-on, built in (neutral):** a per-frame phase timeline — wait, input, playback, evaluation, UI,
   submit, present — as a stacked bar per frame, plus GPU frame time from a ring of timestamp queries read
   2–3 frames late (one facade API, both backends), missed-vblank count against `RefreshPeriodSec`, and a
   **hitch log** that records frames above 1.5× the period together with what else happened that frame
   (GC collection count changed, shader compiled, pipeline created, asset loaded, project saved).
2. **Built-in benchmark:** promote today's probe into the Performance window — "Measure 10 s" with vsync on
   and off, KPI summary, CSV, and a `getMetrics`/`measure` bridge call so agents can regression-test pacing.
3. **Opt-in per-op profiling:** `Plan_OpGraphProfiling.md` as designed (self/inclusive CPU time in
   `Slot.Update` behind a static flag, GPU timestamp pairs, MagGraph heat overlay).
4. **External tools, documented in `.help/`:** PresentMon first for anything pacing-related (present mode,
   displayed time, dropped frames; works for D3D11 and Vulkan), RenderDoc for frame captures (both backends,
   in-app capture API), Nsight Systems for CPU/GPU timelines, Superluminal or `dotnet-trace` for managed
   CPU and GC pauses.
5. **Tracy instead of Unreal Insights.** Insights is tied to Unreal's trace runtime and license. Tracy is
   BSD-licensed, cross-platform, and covers CPU zones, frame marks, plots, and GPU zones. Its C API accepts
   externally measured GPU timestamps, so the backend-neutral timestamp ring from layer 1 can feed it on both
   backends. C# needs a thin P/Invoke layer over that C API; keep it behind a build flag so release builds
   carry nothing.

## 8. Cluster F — Export and Live Video Throughput (user item 6)

- **Export is paced by vsync.** Nothing changes `UseVSync`, the waitable or `Present` during export, so
  export throughput is capped at the display refresh. Present with interval 0 (or skip presenting the main
  window every other frame) while exporting. Neutral, and probably the largest cheap export speed-up.
- **Synchronous readback and encode on the main thread** (`FfmpegVideoExportWriter.cs:66-85`: compute
  convert, `CopyResource`, `Flush`, blocking `Map`; then `sws_scale` + encode + mux). A 3-deep staging ring
  read two frames late plus an encoder thread would overlap GPU, readback and encode. Neutral, and needed
  anyway on Vulkan where `Map(Read)` drains the GPU.
- **Rational frame rates** (23.976, 29.97, 59.94) are not supported: fps is rounded to an int
  (`FfmpegVideoExport.cs:157`), and audio samples per frame are rounded (`AudioRendering.cs:200`).
- **`FrameSpeedFactor = fps/60` during export** is a workaround for per-frame simulations (Cluster A3).
- **Live outputs:** output-setup outputs evaluate before `Playback.Update` (one frame behind the UI) and may
  evaluate twice when the output window doesn't reuse the composite. NDI advertises the configured
  `FrameRate` (default 60), not the real render rate. `NdiInput` drops everything below 30 fps and leaks a
  frame when its upload action is replaced.
- **`OpNotReady` ordering:** it is reset after the output window evaluates but read by `RenderProcess` early
  in the next frame, so the export gate may never see "not ready". Unverified; worth a manual test with a
  slow-decoding video.

## 9. Cluster G — Exposing Timing to Experienced Users (user item 6)

A "Presentation" section in Settings and a matching Player startup option set:

| Setting | Values | Default |
|---|---|---|
| Present mode | Vsync / Low latency / Uncapped | Vsync |
| Frame latency | Low latency (1) / Throughput (2) | 1 until the visual clock lands; then by use case (C4) |
| Audio-visual offset | ms, plus "compensate queued frames" | compensate for soundtracks, not for live input |
| Target rate | Display / Half display / Fixed … | Display |
| Pacing display | Main window / Output … | Main window; first bound output once outputs are bound |

Live readouts next to them: detected refresh per display, achieved fps, missed vblanks, GPU time, present
wait, latency estimate. Output Setup shows refresh and adapter per bound display with a mismatch warning.
A `.help/` page explains the trade-offs (latency vs throughput, why vsync off is not "faster" in windowed
mode, when to use half rate).

## 10. Further Topics Worth Planning

- **GC pauses** are a hitch source that the hitch log should attribute. Consider
  `GCSettings.LatencyMode = SustainedLowLatency` while playing or presenting a show.
- **Windows power management:** Windows 11 throttles background processes (EcoQoS) — an editor driving a
  projector while another app has focus can lose frames. Opt out via `SetProcessInformation`
  (`ProcessPowerThrottling`) for the Player and while outputs are bound; consider MMCSS registration for the
  render thread. Neutral in spirit, Windows API in practice (and the Vulkan build still runs on Windows).
- **VRR (G-Sync/FreeSync):** requires `ALLOW_TEARING` on DXGI or IMMEDIATE/MAILBOX on Vulkan; changes what
  "refresh period" means. Detect and disable cadence snapping when VRR is active.
- **Determinism:** a time-based simulation produces different results at different frame rates unless it is
  sub-stepped. Exports should be reproducible regardless of the display the editor ran on.
- **Input latency** as a measured quantity (photodiode or camera; `output-setup/camera-calibration.md`
  already plans render-to-projector latency).
- **Multi-machine sync** (`output-setup/multi-machine.md`): present-at-time needs present timing; on Vulkan
  that is `VK_EXT_present_timing`.
- **HDR output** changes swap-chain formats and composition paths; keep it in mind when touching swap-chain
  creation.
- **Testing:** a frame-pacing regression test via the debug bridge (`measure` → KPIs) run on a reference
  machine before releases, since CI has no display.

## 11. Suggested Sequence

| Phase | Items | Backend |
|---|---|---|
| **0 — Quick wins** | Done 2026-10-10: frame-profiling channel capped; frame metric recorded from the frame loop; Present in CSV; `IsRenderingToFile` clock switch fixed; springs sub-stepped instead of clamped. Vsync-off cap confirmed (parent of FlipDiscard: 4.63 ms, FlipDiscard: 8.42 ms). Open: PresentMon check of latency 2 | Neutral |
| **1 — Know the display** | Done: `Core/Stats/FrameTiming` measures frame and refresh period (Editor + Player); app-bar graph and its marker use the measured rate; `FrameSpeedFactor` is measured live (exporter's value only while rendering to file); refresh rate in the Performance window and `getMetrics`. Open: built-in "Measure 10 s" (UI design pending); `PerformanceMetrics` window in seconds and buckets relative to the period | Neutral |
| **2 — Stable time** | First slice done 2026-10-11: visual clock in `FrameTiming` (one refresh period per frame under vsync, catch-up in whole periods once a full period off the wall clock); live `Playback` and `LastFrameDuration` use it; the Player measures at frame start. Replay of the bisect recordings: wall std ~10 ms → visual std 0–0.96 ms, drift < 1 period. Beat timing (tapping, phase model) samples `FrameTiming.VisualFrameTimeSec` instead of reading `RunTimeInSecs` mid-frame; `DanceAiPhaseTracker` extrapolates to a given time. Onset-detection beat lock: `BeatSynchronizer.GetBarProgress(time)` extrapolates a lock-protected snapshot of the audio-callback clock to the visual frame time, phase corrections decay over ~100 ms instead of jumping, and the 10-frame average (~75 ms lag) is gone — tested with music 2026-10-11. Fixed step: `Playback.FixedFrameDurationSec` overrides `LastFrameDuration` — export sets 1/fps, `VisualTest` 1/60 s per evaluation step (restored in `finally`). Damping helper: `MathUtils.DampingForFrame` / `BlendForFrame` (identical at 60 fps) applied to `DampFunctions.LinearDamp` (Damp, DampAngle, DampFloatList), DampVec2/3, DetectPulse, DampPeakDecay, MidiInput, OrbitCamera, ActionCamera (also moved off `RunTimeInSecs`), and editor UI smoothing (MagGraph item/section/connection positions, tour, drag speed, sliders, color popup, curve plot, data-set range, dope sheet range, perf-graph peaks) using `FrameTiming.VisualDeltaSec`. Deliberately left: Spring/SpringVec2/3 and SpaceMouse (per-frame integrators, belong with simulations), RadialSlider angle velocity (per-frame mouse delta), AmplifyValues / AudioReaction / LogMessage (smooth per input sample or measurement, not per frame). Not yet: OS refresh query. Remaining:  `Playback` fixed-step mode shared by export and visual tests; `DampFactor` sweep (editor first, then ops); time-based shader sims, then recreate reference images once; A/V offsets in seconds; latency setting; `DetectBpm` time-based; export without vsync + async readback + encoder thread | Neutral |
| **3 — Merge (after 4.3)** | Record the 4.3 probe baseline before merging; after the merge probe D3D11-through-facade and Vulkan against it; GPU queries on both backends | Facade |
| **3b — 4.4 gate** | Latency-1/2 equivalent on Vulkan (`present_wait`); one pacing swapchain, non-blocking secondaries; present timing incl. acquire; Vulkan probe results match the 4.3 baseline | Vulkan |
| **4 — Vulkan era** | Present-mode and target-rate settings, including uncapped "vsync off" (`ALLOW_TEARING` swap chain only while vsync is off, recreated on toggle — decided 2026-10-10 to do it with the other presentation controls, see C1); half-rate via `present_wait`; Tracy behind a build flag; per-op profiling; present timing for multi-machine; VRR | Vulkan-later |

## Decisions (2026-10-10)

1. **Simulation mode:** time-based for all projects; a slightly different look at non-60 Hz rates is fine.
   Visual reference tests and export use a fixed reference step so recreated reference images stay valid (A3).
2. **Pacing display:** tentatively the output display whenever an output is bound (C2); to be verified.
3. **Frame latency:** a use-case setting, not a fixed answer. Live audio input → latency 1; soundtrack and
   beat-locked tapping → latency 2, with the offset compensated. Only after the visual clock lands (C4).
4. **Vulkan default on Windows:** 4.3 ships on D3D11 from main and the linux-port branch merges after 4.3.
   Vulkan as the Windows default is a 4.4 question, decided by the 4.4 gate measurements (D, phase 3b).
5. **Buffers vs lifetimes:** buffer and history lengths stay in frames and are never adjusted automatically;
   lifetimes and durations are seconds (A3).
