# DX11 Render Profiling & Frame-Time Stability

Goal: understand, measure, and reduce the frame-time oscillation currently visible in TiXL, and decouple "how long did I take to build the frame" from "how much time should a simulation advance." The existing on-screen performance overlay (`T3.Core.Stats.PerformanceMetrics`, `MetricGraphView`, `FrameTimeGrader`) is the display surface — this plan is the data that feeds it and the engine changes that make the data meaningful.

## Context

TiXL currently uses `Playback.LastFrameDuration` (wall-clock delta between `Playback.Update()` calls, in seconds) as a single "frame duration" everywhere:

- `AudioEngine.CompleteFrame(playback, frameDuration)` — audio mixer step / clip advancement (Core/Audio).
- `Operators/Lib/render/_dx11/api/TimeConstBuffer.cs` — uploaded to shaders as `LastFrameDuration` for per-op effects.
- `Operators/Lib/numbers/anim/time/LastFrameDuration.cs` — operator that exposes it to the graph (particles, physics, camera movement, anything time-stepped).
- `MathUtils.SpringDamp` and related helpers clamp it to `1/60` before integration (`MathUtils.cs:711, 722, 730`) — an existing workaround for the oscillation problem.
- `CameraInteraction` — camera smoothing (`CameraInteraction.cs:45`, clamped to `4/60`).

The single source conflates three semantically different quantities and is the root of several symptoms:

1. **Simulation judder.** When wall-clock delta oscillates (see "present queue oscillation" below), particle emitters and springs integrate with alternating large/small steps. `MathUtils` clamps exist precisely because this blew up otherwise.
2. **No GPU cost visibility.** We cannot tell "GPU struggling" from "CPU waiting on Present." Every hitch looks the same in the overlay.
3. **Audio coupling.** Audio advancement uses the same wall-clock dt — it should be driven by the audio device clock, not the render thread.

The swap-chain is `SwapEffect.Discard` with `BufferCount = 2` (`Editor/App/AppWindow.cs:47-48`), with a commented-out `FlipDiscard` alternative. No `SetMaximumFrameLatency`, no waitable object. Player uses identical pattern (`Program.RenderLoop.cs:48`). This is the legacy bitblt model; on Windows 10+, DWM silently re-composites which adds 1 frame of latency and is the primary source of the fast-then-slow oscillation.

## Scope

In scope:
- GPU timing via `ID3D11Query` (timestamp / disjoint pair).
- Waitable swapchain + flip-model migration.
- Splitting the single `LastFrameDuration` into semantically distinct clocks.
- DXGI frame statistics for present / display timing.
- Extending `PerformanceMetrics` with the new signals.
- A present-timeline visualisation in the editor overlay.
- Documenting integration with external tools (PIX, GPUView, RenderDoc).

Out of scope (explicitly):
- Fullscreen-exclusive path. Not used; adds surface area, no benefit for TiXL's workflow.
- D3D12 migration. Separate, larger effort.
- Per-operator GPU profiling (requires query-per-op pool) — mentioned as follow-up only.
- Changing the playback clock model (bars/BPM). Only the dt *feeding* playback changes.

---

## Part 1 — Metrics to Capture

Each entry below is a candidate `RollingMetric` in `T3.Core.Stats.PerformanceMetrics` (or a successor registry).

### Already captured
- **Frame duration (wall-clock)** — `ImGui.GetIO().DeltaTime * 1000` / `Playback.LastFrameDuration * 1000`. Current meaning: time between two `Playback.Update()` calls. Keep, rename to `FrameDurationWall` to clarify it is *not* GPU nor display.
- **UI render duration** — CPU time of ImGui build+render. Keep.
- **GC allocations / frame** — Keep.

### New — GPU side
- **GPU frame time** — via `ID3D11Query` disjoint+timestamp pair spanning `BeginFrame..EndFrame`. Two timestamp queries + one disjoint query per frame, double-buffered across 3 frames so we don't stall waiting for results. Unit: ms. This is the "is the GPU struggling" signal.
- **GPU UI pass time** — timestamp pair around the ImGui draw call list. Lets us separate editor-UI cost from scene cost.
- **GPU scene pass time** — timestamp pair around the main output render. Derived: `GPU frame - GPU UI`.
- *(Follow-up, not v1)* **Per-op GPU time** — same pattern, scoped to `SymbolChild.Evaluate`. Requires a query pool sized to ~number-of-ops-per-frame. Gated behind a debug flag.

### New — Present / display side (DXGI)
- **Present wait time** — measured as wall-clock elapsed inside `SwapChain.Present()`. A non-zero value means the present queue was saturated.
- **Time since last vsync** — `DXGI.Output.WaitForVBlank` probe every N frames *or* `SwapChain.GetFrameStatistics()` (`SyncQPCTime`, `PresentRefreshCount`). Better: use the waitable swapchain handle (Part 2) and measure the gap between waits.
- **Missed vsyncs / sec** — derived from `DXGI_FRAME_STATISTICS.PresentRefreshCount` deltas. A present that bumps PresentRefreshCount by >1 missed one or more refreshes.
- **Detected refresh rate** — from `GetFrameStatistics` (`SyncRefreshCount` vs. `SyncQPCTime`). Replaces the heuristic in `FrameTimeGrader.SnapToCommonTarget`.

### New — Stability / distribution
- **Frame-time IQR (P75 − P25)** — captures the oscillation signature (paired fast+slow frames) that one-sided P99 misses. Added to `FrameTimeGrader`.
- **Present-wait IQR** — same pattern on the Present-wait metric; isolates DWM/present-queue thrashing from GPU cost.

### Capture points
- **CPU wall-clock** — continue in `Playback.Update()` (existing).
- **GPU timestamps** — begin/end in `EvaluateAndDrawOutput()` (Player) and the equivalent editor render path (`ProgramWindows`, around `Main.SwapChain.Present`).
- **Present-wait** — `Stopwatch` around the `Present()` call in `ProgramWindows.cs:323` / `Program.RenderLoop.cs:48`.
- **Frame statistics** — poll `SwapChain.GetFrameStatistics()` each frame after Present; handle `DXGI_ERROR_FRAME_STATISTICS_DISJOINT`.

Double-buffering GPU queries: keep `ID3D11Query` instances in a ring of 3 `FrameQueries` records. On frame N, issue queries; on frame N-2, read results (guaranteed ready). Never call `GetData` with spin — always flag-check and skip if not ready.

---

## Part 2 — Swap-Chain Changes

Context: the oscillation I've described (fast frame immediately following a slow one) is the signature of a saturated present queue under the `Discard` swap-effect + no latency waitable. On Windows 10+, DWM composition silently injects an extra queued frame, so `MaximumFrameLatency` defaults behave like 3-4 queued frames.

### Change 2.1 — Migrate main + viewer swap chains to `FlipDiscard`
- `Editor/App/AppWindow.cs:47-48` — swap `SwapEffect.Discard` (bitblt model) for `SwapEffect.FlipDiscard`.
- Bitblt-model requirements that change under flip-discard:
  - Cannot render directly to the back buffer with `PresentFlags.DoNotSequence` tricks. TiXL doesn't use these — safe.
  - Back buffer is not guaranteed to retain contents. We always `ClearRenderTargetView` — safe.
  - No `GDI-interop` (Win32 `HBITMAP` reads on the back buffer). Not used.
  - Format must be in the flip-compatible set (`R8G8B8A8_UNorm` is — current format).
- `BufferCount` must be ≥ 2; bump to 3 to decouple GPU from present.
- Validation: tearing absence in windowed mode, no flickering on window resize (requires `ResizeBuffers` with `Flags = 0` — already the case).

### Change 2.2 — Enable waitable swapchain
- Add `SwapChainFlags.FrameLatencyWaitableObject` to swap-chain description.
- After creation, cast to `SwapChain2`, call `GetFrameLatencyWaitableObject()`, store the handle.
- Call `SetMaximumFrameLatency(1)` or `(2)` (two is safer for mixed GPU/CPU bottleneck; one minimises input lag).
- At the **start** of each frame (before `Playback.Update()`), call `WaitForSingleObjectEx(handle, 1000, true)`. This replaces Present's implicit wait with an explicit, measurable one.
- The wall-clock delta between two successive waits becomes a stable approximation of the display refresh interval, ending the oscillation at its source.

### Change 2.3 — Make `FrameSpeedFactor` path aware of the new clock
- `Playback.FrameSpeedFactor` doc (`Playback.cs:82-87`) already acknowledges simulation ops should scale by it. After Part 3 this becomes redundant for many ops — keep the property for explicit slow-motion / fast-forward but remove it from dt clamping paths.

### Risks
- Flip-discard + BufferCount=3 + MaximumFrameLatency=1 can cause hitches on integrated GPUs under memory pressure (more back buffers). Mitigation: expose `T3Ui.PresentMode` user setting (Low-latency / Balanced / Throughput) that picks one of three presets. Default = Balanced (flip-discard, 2 buffers, latency 2).
- Wine compatibility: the waitable swapchain path works under recent Proton/Wine but was historically flaky. Gate behind a runtime capability check; fall back to classic blocking Present on Wine or if swapchain creation with the flag fails.
- `ResizeBuffers` semantics differ under flip-discard — ensure all viewer windows use the same path (`ProgramWindows.cs:209, 219`).

---

## Part 3 — Splitting `LastFrameDuration`

Proposal: three distinct clocks, chosen by consumer intent.

| Clock | Source | Consumers | Why |
|---|---|---|---|
| `Playback.WallDeltaSeconds` | current `RunTimeWatch` delta | profiling overlay, per-frame metric update | Raw measurement. Noisy under present oscillation. |
| `Playback.VisualDeltaSeconds` | smoothed wall delta: median of last 3 frames, clamped to `[0.5 × target, 2 × target]` where target is detected refresh period | simulation ops (particles, feedback, springs), `CameraInteraction`, shader `LastFrameDuration` uniform | Stable dt for integrators. Eliminates the fast-after-slow pair. |
| `Playback.AudioDeltaSeconds` | audio device clock delta (BASS exposes a sample-position clock) | `AudioEngine.CompleteFrame`, anything that advances `TimeInBars` while playing | Decouples playback position from render jitter. Audio runs on its own thread; render jitter should not retime audio. |

### Call-site audit
Each current user of `Playback.LastFrameDuration` maps to exactly one of the new clocks:

| Call site | New clock | Notes |
|---|---|---|
| `MathUtils.cs:711,722,730` (SpringDamp) | `VisualDeltaSeconds` | Remove the ad-hoc `Clamp(0, 1/60f)` — the new clock is already bounded. |
| `CameraInteraction.cs:45` | `VisualDeltaSeconds` | Remove the `ClampMax(4/60f)`. |
| `T3Ui.Update.cs:49` → `AudioEngine.CompleteFrame` | `AudioDeltaSeconds` | Audio drives itself; wall-clock was a proxy only. |
| `Player/Program.RenderLoop.cs:42` → `AudioEngine.CompleteFrame` | `AudioDeltaSeconds` | Same. |
| `Player/Program.RenderLoop.cs:51` → `PerformanceMetrics.RecordFrame` | `WallDeltaSeconds` | Metric must measure raw wall-clock or we lose the oscillation signal. |
| `TimeConstBuffer.cs:24` | `VisualDeltaSeconds` | Shader effects that derive displacement / trails from dt. |
| `LastFrameDuration.cs:19` (operator) | `VisualDeltaSeconds` | This is what graph authors will use for most cases. Consider exposing a second operator `LastFrameDurationWall` for profiling graphs; keep the existing one as-is for backwards compat. |
| `BlendActions.cs:244` (FIXME) | `VisualDeltaSeconds` | Resolves the existing FIXME. |

### Backwards compatibility
- `Playback.LastFrameDuration` stays, aliased to `VisualDeltaSeconds` — this matches what almost every current consumer *intended*. No existing project files / operators break.
- Add `LastFrameDurationWall` and `LastFrameDurationAudio` as new public statics. Operators can opt in.
- `FrameSpeedFactor` remains, but `VisualDeltaSeconds` applies it — simulation ops no longer need to multiply manually. Clarify in `Playback` doc comment.

### Smoothing strategy for `VisualDeltaSeconds`
- Keep a 3-sample ring of the last three wall deltas.
- Take the median.
- Snap to the detected refresh period if within ±15% (same mechanism as `FrameTimeGrader.SnapToCommonTarget`).
- Clamp to `[target/4, target*4]` as a final safety net.
- Cost: ~nothing. Three compares + a clamp.

This is deliberately simpler than a PID / Kalman filter. Median-of-3 kills the fast-after-slow pair, which is the dominant failure mode. More sophisticated filtering can be added later without changing the API.

---

## Part 4 — Audio Coupling

Audio is sample-clocked (BASS exposes `BASS_ChannelBytes2Seconds` / `BASS_ChannelGetPosition`). The current use of `frameDurationInSeconds` in `AudioEngine.CompleteFrame` is historical — audio stepping should reference the audio device clock directly.

### Investigation tasks
- Trace `AudioEngine.CompleteFrame`'s use of `frameDurationInSeconds` (Core/Audio/AudioEngine.cs:174). Determine whether the value is used for actual sample-accurate advancement or only for stale-detection windows / debounce timers.
- If used for advancement: swap to audio-device clock delta. This removes coupling between render hitches and audio pitch/time.
- If used only for timeouts / debounce: keep the signature but pass `AudioDeltaSeconds` (same thing under the hood but semantically tagged).

### Note
`AUDIO_ARCHITECTURE.md:436` describes a frame-token system (`_audioFrameToken`) for tracking which audio ops updated each frame. That logic is unaffected — the token increments once per `CompleteFrame` regardless of dt source.

---

## Part 5 — Visualisation & Profiling Tools

### In-editor
- **Frame-time plot + histogram** — already landed via `MetricGraphView` + `PerformanceMetrics`. Extend to show the new metrics (GPU, Present-wait, IQR) as stacked graphs.
- **Present timeline** — horizontal strip, one row per frame, showing for the last ~100 frames:
  - CPU build (ImGui.DeltaTime minus GPU wait)
  - GPU busy (from timestamp queries)
  - Present wait (Stopwatch around Present)
  - Vsync interval (from `GetFrameStatistics`)

  Stacked bars, colour-coded. This makes present-queue oscillation visually obvious (alternating row heights). Home for this widget: new button in the app-bar near the existing performance graph; opens a popup with the timeline.
- **Letter grade + score** — hook `FrameTimeGrader.Result` to the existing overlay. Score drives a colour ramp on the top-bar performance button (green A+ → red F).

### External tools (for deep-dive debugging, not replacement)
- **PIX on Windows** — frame capture + timing. Good for per-draw-call GPU cost. Document the `.pix` capture workflow in `.help/` once integration is stable.
- **RenderDoc** — structural capture (shaders, resources, state). Not a timing tool but pairs well with PIX.
- **GPUView** (Windows Performance Toolkit) — system-wide queue timeline. The gold standard for diagnosing DWM / present-queue issues. Capture via `xperf` → view in GPUView.exe. Identifies whether a hitch is in: app CPU, driver CPU, GPU, DWM, or display. Document capture command + .etl workflow in `.help/`.
- **Nvidia Nsight Graphics** / **Nvidia FrameView** — vendor-specific, high-fidelity GPU timing. Optional.
- **Intel GPA** — same, for Intel GPUs.
- **AMD Radeon GPU Profiler** — same, AMD.
- **Windows PresentMon** (open source, from Intel) — minimal-overhead command-line tool that logs present / display timing. Great for regression tests: CSV output → script-driven comparison of two builds.

### Suggested workflow
1. Notice a hitch in the in-editor timeline.
2. If it's a GPU-time hitch → PIX capture on that frame.
3. If it's a Present-wait hitch → GPUView capture over ~5 seconds straddling the hitch.
4. If the hitch is a whole-system thing (input, audio, disk) → Windows Performance Analyzer with the same capture.

---

## Part 6 — Stability Experiments

This section tracks concrete experiments whose goal is to reduce frame-time variance. Each one is a *controlled change* we can toggle and measure; the per-experiment pass/fail criterion is always a drop in `FrameDurationWall` IQR and `PresentWait` IQR over a one-minute window on a representative project.

Experiments are grouped by where the lever lives.

### 6.1 — DXGI / swap-chain levers

| Experiment | Change | Expected effect | Risk |
|---|---|---|---|
| A | `SwapEffect.Discard` → `SwapEffect.FlipDiscard` | Removes DWM bitblt re-copy, tighter present scheduling | Requires all back-buffer access assumptions to hold (see Part 2). Low. |
| B | `BufferCount = 2 → 3` | Decouples GPU from present queue; fewer stalls | More video memory; on 4 GB GPUs could push edge cases. |
| C | `SetMaximumFrameLatency(1)` | Shortest queue, lowest input lag | CPU/GPU stalls if frame build is uneven. |
| D | `SetMaximumFrameLatency(2)` + waitable object | Balanced: GPU can get ahead by 1, waitable removes Present blocking | Most complex path. This is the proposed Balanced default. |
| E | `Present(1, DoNotWait)` vs. `Present(1, None)` | `DoNotWait` can return `DXGI_ERROR_WAS_STILL_DRAWING` — useful only to measure how often the queue is saturated | Adds error-handling surface. Measurement-only. |
| F | Disable vsync (`Present(0, None)`) on a dedicated diagnostic build | Bounds the "pure GPU cost" measurement — any remaining oscillation is app-side, not display-side | Tearing; diagnostic use only. |

### 6.2 — Driver / OS levers (verify via measurement, don't ship as defaults)
- **Nvidia Reflex low-latency mode** (driver panel). If a user reports hitches and has it off, ask them to turn it on as a first-line test. Not something TiXL sets itself.
- **Nvidia "Max Frame Rate" limiter** vs. our Present-wait — can be used as a diagnostic to see whether the oscillation is coming from the app or the driver's own pacing.
- **Windows HDR** on the output. HDR adds a composition pass; disable when diagnosing to isolate.
- **Hardware-accelerated GPU scheduling** (HAGS, Win10 2004+). Toggle and re-measure. HAGS generally reduces latency but has been implicated in occasional stutter regressions per-driver-version.

### 6.3 — Application-side levers
- **Busy-spin at end of frame** — deliberately sleep until `now >= lastPresent + targetFramePeriod - safety`. Emulates what RTSS does. Cheap experiment, sometimes reduces variance by ~30%.
- **Pre-warm query pool** — issue dummy timestamp queries for the first ~30 frames to let the driver settle before trusting measurements.
- **Eliminate per-frame allocation spikes** — correlate the GC spike histogram (already captured) with frame-time spikes. Any bucket above ~200 kB/frame that correlates with a latency bucket is a smoking gun.

### 6.4 — Measurement protocol for each experiment
- Fixed project, fixed window size, warm-start (skip first 5 s).
- Window of 60 s or 3600 frames, whichever is shorter.
- Metrics: mean, P50, P95, P99, IQR of `FrameDurationWall` and `PresentWait`; mean `GPUFrameTime`; `MissedVsyncsPerSec`.
- Run each variant 3× and median-of-3 the summary. Single runs can be dominated by DWM mood swings.
- Export: `PerformanceMetrics` already has the data; add a "Dump to CSV" button in the profiling overlay so runs are directly comparable.

---

## Part 7 — User-Facing Performance Settings

Most of the levers above don't belong as user settings (too technical, too easy to mis-tune). A small subset does — for live-performance users who need predictable latency, and for install operators targeting specific hardware.

### Proposed `Settings` → `Advanced → Render Performance` panel

| Setting | Values | Default | Notes |
|---|---|---|---|
| **Present mode** | Low-latency / Balanced / Throughput | Balanced | Picks `SwapEffect`, `BufferCount`, `MaximumFrameLatency` preset (see Part 2 table). |
| **Vsync** | On / Off / Adaptive | On | Already a runtime toggle (`T3Ui.UseVSync`); promote to persistent setting. Adaptive = vsync when above target, tear when below (requires checking driver support). |
| **Max frame rate** | Off / 30 / 60 / 120 / 144 / Display | Display | Useful for laptops on battery and for keeping GPU cool during long installs. Implemented as a busy-wait to the target frame period at end-of-frame. |
| **GPU profiling** | Off / Global / Per-pass / Per-op | Global | Gates the depth of `ID3D11Query` pooling. Per-op is expensive and debug-only. |
| **Present timeline overlay** | Off / On | Off | The stacked-bar visualiser from Part 5. Off by default (distracting during normal work). |
| **Audio clock source** | Render frame / Audio device | Audio device | Exposed because if M4 audio decoupling ever regresses on a specific sound card, users need an escape hatch. |

Settings live in the existing `UserSettings` (`Editor/Gui/UiHelpers/UserSettings.cs`). **Do not** expose these in per-project settings — they're machine-local. Persistence in `UserSettings.json`.

### Principle for exposing vs. not exposing
A setting is worth exposing only if:
1. The *right* value depends on the user's hardware or use-case (laptop on battery vs. live-performance rig), **and**
2. A plausible user can read the setting's name and pick the right value without reading source.

Everything else stays hardcoded with a single well-tested default. Specifically: `MaximumFrameLatency` numeric value, query-pool size, smoothing window length, and threshold constants in `FrameTimeGrader` — these are not user settings.

---

## Part 8 — Cross-GPU and Cross-Machine Frame Sync

Relevant for: multi-projector walls, LED installations with N render PCs per wall, multi-display live sets where one machine drives two GPUs. This section is forward-looking — nothing about it is needed for the single-machine experience.

### 8.1 — Inside one machine: multi-GPU
Current TiXL uses a single `ID3D11Device`. Moving to multi-GPU (one adapter per output) would require:
- Per-adapter `Device` + `SwapChain` pairs.
- A shared CPU clock that each output's render loop locks to (Stopwatch timestamp at `Present-wait release`).
- Explicit cross-adapter sync via shared textures (`D3D11_RESOURCE_MISC_SHARED_NTHANDLE`) or CPU-side fence objects.

Out of scope for this plan. Flagged here so future-us doesn't design the single-GPU clock split (Part 3) in a way that precludes multi-GPU later. Specifically: `PerformanceMetrics` and the clock sources must be keyed by render target, not static — add a `RenderContext` parameter to `RecordFrame()` when we get there.

### 8.2 — Across machines: genlock and frame groups
For installations where multiple PCs render to a single seamless wall, "frame groups" (sometimes spelled "framelock") keep every machine's presented frame aligned so seams don't show tearing/desync. Three levels:

| Level | Mechanism | TiXL implication |
|---|---|---|
| **Software sync** | UDP/TCP broadcast of a "present now" pulse from a master; slaves busy-wait until pulse + offset, then Present | Implementable as a small networking module; no special hardware. ±1 ms typical accuracy. Adequate for many installs. |
| **Timecode sync** | MTC / LTC / Art-Net timecode → each node aligns its playback clock to the timecode stream | Orthogonal concern: timecode aligns *content time*, not *frame presentation*. Usually combined with software or hardware sync for the actual display alignment. |
| **Hardware genlock** | NVIDIA Quadro Sync / AMD FirePro S400 — BNC cables between cards; frame presentation driven by an external sync signal | Requires pro GPUs. Not something TiXL opts into — if present, the driver does the work and `GetFrameStatistics` reports aligned vsync times automatically. We just need to *not break* the alignment. |

Design requirement for TiXL today (even before any of the above ships): **the clock split in Part 3 must not introduce per-machine drift**. If `VisualDeltaSeconds` is median-smoothed locally on each node, two nodes seeing the same wall-clock sequence will produce identical smoothed values — good. But any jitter added by one node's DWM will *not* propagate to peers, which means different peers will step simulations slightly differently over time. Solutions:
- For simulation ops (particles): advance based on a *shared* time source broadcast by the master, not local dt.
- For visual-only effects (shader-driven `LastFrameDuration` uniform): local is fine.

This is in-scope for this plan only as a *design constraint* — we tag `VisualDeltaSeconds` as "node-local" and document that simulation-synced installs must subscribe to an external clock. Actual network-sync implementation is a separate plan.

### 8.3 — NDI / video output paths
If TiXL is already used as an NDI sender (verify; `Operators/Ndi`), the NDI stream carries its own timecode. A receiver machine slaves to that timecode. In this topology, TiXL-as-sender should emit its frames at a steady cadence — which is exactly what the waitable swapchain buys us. No special code; the improvements in Part 2 already help.

---

## Part 9 — Audio-Presentation Sync

Goal: minimise and quantify the offset between what the user *hears* and what they *see* for audio-reactive visuals.

### 9.1 — Where latency comes from
End-to-end audio→photons path, in rough order:
1. **Audio decode / DSP** (BASS pipeline). ~1–10 ms depending on effects.
2. **Audio buffer** (BASS output buffer). Typically 20–50 ms; user-configurable.
3. **Audio device** (ASIO vs. WASAPI shared vs. exclusive). 2–20 ms.
4. **Analysis → visual op** (`[AudioReaction]` etc.). Current: samples produced *this* frame are read *this* frame — already tight.
5. **Render** (CPU + GPU build). 5–30 ms.
6. **Present wait** (queue depth). 0–50 ms depending on swap-chain settings.
7. **DWM composition**. 16.7 ms in windowed mode (one DWM vsync).
8. **Display hardware lag**. 5–80 ms. **Unknown and unmeasurable from software.**
9. **Speaker path**. Wired: ~0 ms. Bluetooth: 100–300 ms.

Items 1–7 are software-controllable. 8–9 are not.

### 9.2 — Concrete improvements (software only)
- **Reduce BASS output buffer** — user setting in `Advanced → Audio`. Trade-off: smaller buffer = more underrun risk. Expose as "Audio latency" (Low / Balanced / Safe) mapping to 20 / 40 / 80 ms.
- **WASAPI exclusive mode** — cuts ~10–15 ms vs. shared. Already an option in BASS; verify TiXL path. Risk: blocks other apps from the sound card. Opt-in only.
- **Present-wait reduction** — everything in Part 2 shaves 8–16 ms off the photons side.
- **Frame-ahead scheduling for audio-reactive ops** — if audio is *ahead* of visuals by `Δ` (measured), schedule visual response to sample `t - Δ` so they appear simultaneously. Requires an accurate estimate of `Δ` per frame; see next section.

### 9.3 — Measuring the offset (not just guessing)
- **Audio-to-photons latency probe**: optional calibration mode that plays a click and flashes a full-screen frame simultaneously. User records with a phone; we analyse the audio-video offset from the clip. One-time, writes `AudioVisualOffsetMs` to `UserSettings`. Used thereafter by any op that wants to compensate.
- **Log the queue state** at each layer: BASS output queue fill, Present queue depth, `GetFrameStatistics.PresentRefreshCount` drift. If any of these are consistently non-zero, the static offset estimate above has wiggle room and should be re-probed.

### 9.4 — What we explicitly *cannot* fix from software
- **Display lag** on consumer TVs in non-game mode can be 60–100 ms. Document this in the `.help` page; suggest "Game mode" / "PC mode" on the target display.
- **Wireless audio** (Bluetooth / AirPlay). Document: use wired or Low-Latency Bluetooth codecs (aptX LL, AAC LL) and expect residual 30–100 ms.
- **HDMI audio embedded** may add a frame vs. separate jack out. Measure per-setup.

The probe (9.3) absorbs all of these into a single measured number, which is the honest answer: we can't remove unknowable latency, but we can measure it once and compensate the parts that are software-controllable.

### 9.5 — Cross-reference
If cross-machine sync (Part 8) is in play, per-machine A/V offset must also be measured per-machine. The probe should be per-node.

---

## Open Questions

- **Should the smoothed `VisualDeltaSeconds` be exposed via a separate operator** or replace the existing `LastFrameDuration.cs` operator? Answer depends on whether any shipped projects rely on the *current* noisy behaviour. Audit needed before swapping.
- **Waitable swapchain on the *viewer* window** (`ProgramWindows.cs:219`) — nice-to-have or required? Viewer is typically throughput-bound (rendering to texture for export), not latency-sensitive. Likely keep legacy Present there.
- **GPU query pool sizing** — 3 frames of double-buffered pairs is enough for frame-total timing. A sub-pass pool (UI vs. scene) needs 6 queries in flight. A per-op pool could need 100+ queries. Start with the global pair; grow if warranted.
- **Does BASS expose a monotonic clock usable as `AudioDeltaSeconds`?** Needs verification against the BASS docs and TiXL's existing `AudioEngine` accessors. If not, fall back to a high-resolution QPC tied to the audio output device's stream callback.

---

## Milestones

1. **M1 — Measurement only, no behaviour change.** Add GPU timestamp queries + Present-wait `Stopwatch`. Extend `PerformanceMetrics`. Extend `FrameTimeGrader` with IQR. Land the Present-Timeline widget. "Dump to CSV" button for the experiment protocol in Part 6.4. No swapchain / clock changes.
2. **M2 — Swap-chain flip-model + waitable.** Migrate AppWindow + ProgramWindows viewer. Add Low-latency / Balanced / Throughput preset selector (Part 7 table). Validate under Wine.
3. **M3 — Clock split.** Introduce `WallDeltaSeconds`, `VisualDeltaSeconds`, `AudioDeltaSeconds`. Route call sites per the audit table. Remove ad-hoc clamps in `MathUtils` and `CameraInteraction`. Verify particle / feedback / spring ops behave identically or better.
4. **M4 — Audio decoupling + offset probe.** Investigate and (if applicable) swap `AudioEngine.CompleteFrame` to audio-device clock. Ship the A/V offset calibration probe from Part 9.3. Expose Audio-latency user setting from Part 7.
5. **M5 — Stability experiments.** Run the Part 6.4 protocol across experiments A–F plus the application-side levers (6.3). Write up findings; decide which presets to wire into the Present-mode selector. Publish the CSV dumps in the repo for reproducibility.
6. **M6 — User documentation (final step).** Ship the `.help` pages:
   - `.help/docs/advanced/FrameTimingAndLatency.md` — the 1–2 page overview for end users. Covers what frame time, vsync, present queue, and A/V latency mean; what to expect on different hardware; which settings in Part 7 to reach for in which situation; the A/V offset probe workflow. Written to the style guide in `.help/docs/STYLE.md` (150–400 lines, plain English, second person).
   - Tool-specific capture workflows (PIX, GPUView, PresentMon) as either sub-sections of the main page or linked sibling pages under `advanced/` — decide at write-time based on length.
   - Add the new pages to `advanced/README.md`'s "Pages in this section" list.

Each milestone is independently shippable; M1 produces immediate value (visibility) without risk. M6 is deliberately last: the help page should describe what the feature actually ships as, not what we intended. Writing it at the end also forces a final sanity check — if the page is hard to write, the user-facing surface area is probably wrong.

---

## Appendix — Findings from Initial Allocation Investigation (2026-04-25)

While building the performance overlay we did an exploratory pass on per-frame managed allocations using a temporary instrument that wrapped `Slot.Update` with `GC.GetTotalAllocatedBytes(precise: true)` before/after measurements (with a thread-local stack to subtract child allocations from parent self-time). The instrument was useful enough to be worth documenting, but the wrapping cost is too high for permanent inclusion (~10-30 µs per slot update at 1500+ slots/frame inflates frame time noticeably). It was reverted. Findings below.

### What `precise: true` revealed
- The "GC spike every ~1.5 s" pattern observed in the captured CSVs is **real periodic Gen 1 GC**, not a tracking artifact. Confirmed by sampling `GC.CollectionCount(0/1/2)` alongside the spike threshold logger: nearly every spike line carried `Gen0+1 Gen1+1`. Spike size (~2.5 MB) ≈ Gen 1 segment overhead at the prevailing per-iter allocation rate.
- "Spike-splitting across two frames" (e.g. `2997 + 1866` summing to a normal `~4863` chunk) is the signature of a GC straddling a frame boundary. The first frame's sample sees the GC mid-collection; the next frame catches the spillover.
- Switching from `precise: false` to `precise: true` flattens the *shape* of the allocation noise (no more per-thread alloc-context cache flushes showing up as bursty reporting) but does not remove the Gen 1 spikes themselves — those are real CLR overhead at high allocation rates.

### Top per-op allocators (loop=100 repro scene, ~5 s capture)

| Op class | Bytes / call | Notes |
|---|---|---|
| `ComputeShaderStage` | 398 | SharpDX state-setter marshalling + (since fixed) per-call array literals + `GetRenderTargets(2)` array alloc |
| `SetPixelAndVertexShaderStage` | 296 | `vsStage.GetConstantBuffers/Resources/Samplers` each allocate a fresh array |
| `OutputMergerStage` | 184 | Same pattern as PS+VS |
| `Loop` | 158 | Self-time after subtracting child Updates; suspect `Command.InvalidateGraph()` or context-dictionary churn |
| `Draw` | 69 | DX11 state setters |
| `GetFloatVar` | 74 | Higher than expected for a dictionary lookup; worth a follow-up audit |
| `Rasterizer` | 54 | DX11 state setter |
| `IntsToBuffer` | 43 | Same family as FloatsToBuffer (now ~7) |

### Fixes that landed

These are real wins that survived the cleanup and are part of the codebase:

1. **`Core/Rendering/ResourceUtils.WriteDynamicBufferData<T>`** — the `MapSubresource(..., out _)` overload bound to `out DataStream` and allocated a `DataStream` wrapper per call. Switched to the no-DataStream `MapSubresource(buffer, 0, MapMode, MapFlags)` overload that returns `DataBox` directly. **Estimated savings: ~0.21 kB per Loop iteration.**

2. **`Core/Resource/ResourceManager.UpdateConstBuffer<T>`** — same `DataStream` allocation pattern. Replaced with stack-address-of-value (`&value`) feeding `UpdateSubresource` directly through a `DataBox`. Constraint changed from `where T : struct` to `where T : unmanaged`; all call sites already pass blittable structs. **Estimated savings: ~0.07 kB per Loop iteration** (called by `[TransformsConstBuffer]` per Loop iteration via dirty-flag invalidation).

3. **`Operators/TypeOperators/Gfx/ComputeShaderStage`** — array literals `[-1, 0, -1, -1]`, `[counter]`, `[0, 0]`, `[counter, -1, -1]` were being allocated per call. Cached as static-readonly (constants) and instance fields (counter-dependent, mutated head element). **Estimated savings: ~24-40 bytes per call.**

Aggregate: per-Loop-iteration allocation went from ~1.4 kB to ~0.95 kB. At loop=1000 × 60 fps that's ~27 MB/sec less GC pressure.

### What got blocked

The bigger remaining wins (the ~250-400 b/call across the four DX11 state-setter ops) are all the same pattern: SharpDX 4.2.0's no-allocation `Get*(int, int, T[])` overloads are documented in the SharpDX XML metadata but **not exposed publicly** in the assembly — they appear as `internal` or stripped from the public surface. Also affects the no-allocation `OutputMergerStage.GetRenderTargets(int, RenderTargetView[], out DepthStencilView)` overload.

This isn't a per-op problem; it's an API-surface ceiling in this SharpDX version. Options:

1. **Surgical P/Invoke** of `OMGetRenderTargets`, `VSGetConstantBuffers`, `PSGetConstantBuffers`, etc. directly via `[LibraryImport]`. Each is ~10-20 lines. Acceptable if the savings are needed before Vulkan migration; not essential.
2. **SharpDX → Vortice.Windows migration.** Drop-in replacement, exposes all the no-alloc overloads. Out of scope given Vulkan is the planned long-term target.
3. **Accept the floor.** With the fixes above, per-iter is ~0.95 kB. Gen 1 fires every ~3500 iter at that rate. Tolerable for normal scenes; only stress-test setups (loop=1000 over a non-trivial sub-graph) hit the wall.

### Prior swap-chain FlipDiscard attempt (resolved by ImGui upgrade)

Commit `5046734e` (June 2025) reverted a switch from `SwapEffect.Discard` + `BufferCount = 2` to `SwapEffect.FlipDiscard` + `BufferCount = 3`. The revert reason wasn't documented. **Re-tested April 2026 against the current ImGui.NET 1.91.6.1 — no problems found across the five candidate failure modes** (resize stress, viewer window, MirrorUiOnSecondView toggle, cold-start, dock-out tab rebuilds). The original blocker was almost certainly fixed by the ImGui DX11 backend upgrades between 1.91.0.1 (when the revert happened) and 1.91.6.1.

Current state: ready to land FlipDiscard + BufferCount=3, with `FrameLatencyWaitableObject` as the immediate follow-up unlocked by the swap-effect change.

Important context: the FlipDiscard switch was originally suggested by mrvux, who works in an equivalent C# + SharpDX visual-programming stack and runs flip-model in production successfully. So the design is *known* to work in TiXL-shaped applications. The blocker is therefore TiXL-specific — something in this codebase interacts badly with flip-model semantics.

Investigation order for finding the TiXL-specific issue:

1. **ImGui version drift.** The revert happened at ImGui.NET 1.91.0.1 (June 2025); the current version is 1.91.6.1 (April 2026). Six minor upstream Dear ImGui revisions between those dates touched the DX11 backend's swap-chain handling. **The original blocker may already be fixed by the ImGui bump** — re-attempt cleanly first; if it works, the appendix is mostly historical.
2. **Resize handling.** Flip-model requires specific flags preserved across `ResizeBuffers` (in particular keeping the original `SwapChainFlags` value); bitblt is more forgiving and silently tolerates flag drift.
3. **Second swap chain (`Viewer` in `ProgramWindows.cs`).** Multiple flip-model chains in one process have stricter present-ordering requirements than bitblt.
4. **`MirrorUiOnSecondView` path.** Cross-swap-chain texture sharing semantics differ under flip-model's discard policy.
5. **First-frame artifacts.** Some implementations need a warmup present before stable output.
6. **Sikarugir / Wine.** Flip-model support has varied by Wine version; if Sikarugir testing was happening at the time of the original revert, that's a candidate.

Recommended revised M2:

- **M2a (low-risk slice):** Add `IDXGIDevice1::SetMaximumFrameLatency(2)` on the *existing* bitblt swap chain. Coarser than flip-model's `FrameLatencyWaitableObject`, but a real latency reduction without changing `SwapEffect`. Useful as a baseline that survives even if M2b takes a while.
- **M2b (the real win):** Re-attempt FlipDiscard with a focused bisection of the five candidates above. The fix is whatever TiXL-side code makes the migration safe; the swap-chain config change itself is a known-good design.
- **`FrameLatencyWaitableObject` itself requires flip-model** (the flag is rejected on bitblt swap chains), so the precise frame-pacing benefit ultimately depends on M2b.

---

### Cautionary precedent: the 2025 ImGui buffer upload revert

Commit `edf36f2c` reverted an earlier attempt (`19fcd1b`, July 2025) to apply this same `out DataStream` removal pattern to the ImGui per-frame vertex/index buffer upload in `WindowsUiContentDrawer.cs`. The revert reason: *"did cause UI artifacts with complex graphs."*

Reading the original diff: the failed attempt bundled **two** changes into one commit — (a) the DataStream removal we'd recommend, and (b) moving the index buffer's allocation from constructor-time (always `ushort.MaxValue` slots) to dynamic per-frame growth. The artifacts were almost certainly caused by (b) — a stale `IASetIndexBuffer` binding when the IB grew mid-frame. The pointer arithmetic in (a) is well-formed and would have been safe on its own.

Lesson for any future revisit of this code path: the DataStream removal is fine in isolation, but don't bundle it with buffer-allocation-strategy changes. Use the no-DataStream `MapSubresource(buffer, 0, MapMode, MapFlags)` overload + `Span<byte>` copies (bounds-checked) instead of raw `IntPtr` arithmetic, and leave the IB sizing strategy alone in the same commit.

The two DataStream fixes we landed today (`WriteDynamicBufferData`, `UpdateConstBuffer`) are deliberately scoped to be the inverse of the 2025 attempt: each one is a single mechanical replacement, no buffer-strategy change, no pointer arithmetic, no cross-call state.

### Process lessons
- The `precise: true` flag costs more than the docs suggest. Useful for diagnostic captures, not for live monitoring. The default `precise: false` undercount-then-flush behaviour is itself misleading without context — recommend documenting that `PerformanceMetrics.GcAllocationsKb` numbers are slightly batched.
- The single biggest lever we found across this whole investigation was **not** the allocation work — it was the `SwapEffect.Discard` → `FlipDiscard` swap-chain change (Part 2 / M2) which directly addresses the fast-after-slow oscillation seen in the early frame-time captures. The allocation work moved per-iter cost ~30%, but the swap-chain change reshapes the entire frame-time distribution.
- The temporary `AllocationAttribution` instrument (wrapping `Slot.Update` with paired `precise: true` GC samples) was the right tool for *this* investigation but the wrong tool to keep in the codebase. Documented here in case the same pattern is needed for a future allocation hunt — the implementation lives in this commit's history.

### What kept shipping
Independent of this investigation, the broader performance-monitoring surface delivered:
- `T3.Core.Stats.PerformanceMetrics` registry (FrameDuration, UiRenderDuration, GcAllocationsKb, TotalFrameCount; `RecordFrame`/`RecordUiRender` API; `GcSpikeDetected` event with Gen-counter deltas)
- `T3.Core.Stats.RollingMetric` data structure (sliding-window histogram + min/max via monotonic deque + sliding average)
- `T3.Core.Stats.FrameTimeGrader` (percentile/grade derivation)
- Editor `MetricGraphView` (plot line + histogram + mean triangle + bucket hover with shift-cumulative)
- Editor `PerformanceWindow` (toggleable from app-bar mini graph; same content as the hover tooltip)
- `UserSettings.UseVSync` (promoted from runtime-only flag)
- CSV export button for sharing capture data

These are all preserved; only the experimental allocation-attribution machinery was removed.
