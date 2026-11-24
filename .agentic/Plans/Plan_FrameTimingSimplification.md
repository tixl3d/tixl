# Frame Timing Simplification — One Constant Frame Duration

Status (2026-11-24): Phase 0 measured (§2a). Uncommitted: the 144 Hz lock clamp (`MaxVblanksPerFrame` 4 → 12) and a
constant visual clock (§2a). `present_wait` lock pacing was tried and reverted (§2a).
Grounded in `main` at `9e203d9b5`. §2 lists the hypotheses as first written; §2a says which held.
Background and earlier measurements: [`Plan_FrameTiming.md`](Plan_FrameTiming.md) (research report, partly
superseded by this plan: its visual-clock design is what this plan removes).

## Goal

- One frame duration, constant from frame to frame under vsync, used by **every** operator and UI element that
  integrates over time: damping, springs, cameras, particle and feedback sims, velocity buffers, motion blur.
- It is the display's refresh period (from the OS / SDL display mode, never estimated from frame intervals), or
  the locked frame rate from the Performance window.
- A frame drop is a real frame drop. It is measured and shown, never compensated by speeding up or slowing down
  simulations.
- Fewer code paths. Contributors have pointed out that several frame-duration computations fight each other.

Non-goals: VRR-aware pacing, present-timing-based scheduling, changing export or visual-test determinism.

## 1. What Exists Today

At least five frame durations are mixed:

| Source | Computed in | Used by |
|---|---|---|
| `FrameTiming.VisualDeltaSec` — whole vblanks, 2 %/frame pull towards wall clock, whole-period catch-up; returned by `Playback.LastFrameDuration` | `Core/Stats/FrameTiming.cs` `AdvanceVisualClock` | Damp/Spring ops via `RenderPassState.GetStepDurationSec`, `MathUtils.Dampen*`, cameras, `TimeConstBuffer`, timeline advance, `RepeatFrame`, UI damping |
| `FrameTiming.FramePeriodSec` — 120-frame (~2 s) average of frame-start intervals | `FrameTiming.RecordFrameStart` | `FrameSpeedFactor` → particles / feedback (6 library `.t3` graphs + `GetFrameSpeedFactor`), motion blur shutter |
| `FrameTiming.FrameIntervalSec` — raw | same | Player metrics |
| `ImGui.GetIO().DeltaTime` — own Stopwatch | `UiContentUpdate.TakeMeasurement` | `CameraInteraction`, `GamepadCamera`, `ScalableCanvas`, `EdgeScrolling`, metrics bar, `PerformanceMetrics.RecordFrame` |
| Wall-clock fallback, separate `_lastFrameStart` statics | `Playback.Update`, `BeatTimingPlayback.Update`, `GetLiveFrameDuration` | any frame without `RecordFrameStart` |
| `FrameTiming.VisualFrameTimeSec` | `RecordFrameStart` | `BeatTiming.Update`, `NdiSender.IsDueAtFrameRate` |

Plus `Playback.FixedFrameDurationSec` (export, visual tests) and `ProgramWindows.HoldForTargetFrameRate`
(frame-rate lock via `FrameTiming.VblanksPerFrame`).

## 2. Why It Misbehaves (hypotheses for Phase 0)

1. **Motion-blur flicker.** `_ExecuteMotionBlurPasses.GetShutterScale` uses
   `shutter * FramePeriodSec / LastFrameDuration`: a 2 s average divided by a per-frame quantized value. Each
   time the quantized value flips between 1 and 2 vblanks, blur length halves or doubles for one frame.
2. **Particles disguise drops.** `FrameSpeedFactor` follows the 2 s average, so after a hitch particle speed
   drifts for two seconds, while damping ops step per frame. The two halves of a graph disagree.
3. **8 ↔ 30 ms jumps.** The visual clock infers when a frame was displayed from when the CPU *started* it. With
   frames queued under vsync these are only loosely related. When the achieved rate is not an integer divisor of
   the refresh (e.g. 40–90 fps on 120 Hz) or the start time jitters near a vblank boundary, the whole-period
   catch-up turns sub-ms noise into ±1 refresh period, producing 8.3 / 16.7 / 25 / 33 ms sequences. This is an
   estimator, which we decided to stop building.
4. **Mode switches.** `canPaceByRefresh` and the `RecordedFrameCount` fallback swap between quantized and raw
   durations depending on state (vsync toggle, stepping playback, implausible interval).

## 2a. Measured (Phase 0, 2026-11-24)

Linux / Vulkan, Debug build, isolated editor in the project hub over the bridge (`getFrameSamples` intervals,
`getMetrics`). Load came from `stallMainThread` sent in a loop, so it is CPU-bound and lands on roughly every
frame. The visual clock and the motion-blur shutter scale were **replayed offline** from the intervals with the
exact `AdvanceVisualClock` logic, not read from the editor. Displays: built-in eDP-1 at 120 Hz, external DP-1 at
59.95 Hz (KDE Wayland). The display's real present times were not measured; "shown for N vblanks" below is
inferred from the CPU cadence.

| Case (120 Hz unless noted) | Frame intervals | Replayed visual delta | Shutter scale | `FrameSpeedFactor` |
|---|---|---|---|---|
| idle, latency 2 or 1 | 8.3 ms ±0.4, no misses | constant 8.3 | 1.00 | 2.00 |
| lock 30 (latency 2, ± 20 ms load) | **31.2–32.4 ms, mean 31.6 (= 31.6 fps)** | 33.2 → 32.7 drifting, then **0.0** every ~20 frames | 0.0–0.98 | 0.52–0.53 (nominal 0.50) |
| 18 ms load (latency 2 or 1) | steady 19–20 ms | **16.9 / 17.0 / 25.4 repeating** | 0.78–1.19, every 3rd frame | 0.83 |
| 24 ms load | steady 25–26 ms (3 vblanks) | 25.4, a 33.9 every ~20 frames | 0.77–1.04 | 0.64 |
| external 60 Hz, idle | **50 / 15 / 1.3 ms burst every ~12 frames** (~8 % of vblanks missed) | 1–3 vblanks | 0.35–1.09 | 0.97 (nominal 1.00) |

What held and what did not:

1. **Motion-blur ratio (hypothesis 1): mostly wrong.** `FramePeriodSec / LastFrameDuration` compensates the
   displacement, which also scales with the visual delta, so blur length stays roughly constant. The real flicker
   sources are **visual-delta-0 frames** (no displacement → no blur, damping freezes) and the 2 s average lagging
   the current cadence.
2. **Averaged `FrameSpeedFactor` (hypothesis 2): confirmed.** It sits at 0.83 instead of 2 under load and 0.97
   instead of 1 on the hitching external display, while damping steps per frame.
3. **8 ↔ 30 ms jumps (hypothesis 3): confirmed, but the cause is real cadence.** With a smooth 19.6 ms CPU cadence on
   120 Hz, the display must show frames for 2, 2, 3 vblanks, and the visual clock reproduces that (17/17/25). No
   clock can make that smooth; only a rate the display divides can (40 fps on 120 Hz, which the dropdown lacks).
4. **New: the frame-rate lock does not hold its rate on Vulkan.** `HoldForTargetFrameRate` releases each frame
   `vblanks − 0.25` periods after the previous *release*, and nothing in the loop blocks on the vblank (present
   0.01 ms, latency 1 and 2 alike). So lock 30 runs at 3.75 periods + `Thread.Sleep` overshoot ≈ 31.6 fps; the
   display presumably shows a 3-vblank frame every ~5 frames, and the visual clock hides it with a 0-delta frame
   every ~20. This breaks the NDI-at-30 case (§3.6): 31.6 fps sent under a declared 30.
5. **New: the Frame Latency setting is dead.** `UserSettings.Config.FrameLatency` is read only by its dropdown and the
   bridge; neither swapchain uses it.
6. **New: the external 60 Hz output hitches while idle.** A 3-vblank stall plus a catch-up frame every ~200 ms,
   with < 2 ms CPU. A real drop, outside the timing code (compositor with mixed-rate outputs?). Worth its own
   investigation; not measured with latency 1 or a clean lock there.

Consequences for §3:

- **Pacing the lock from `present_wait` — tried and reverted.** Rendering right after the previous present and
  holding only the present until just past the (n−1)th refresh after the previous frame was reported shown held a
  30 lock exactly at 60 Hz (20–30 ms load, every frame 33.2–33.6 ms) but not at 120 Hz: lock 30 showed 6–74 % of
  frames at 5 refreshes, lock 60 28 % at 3, depending on phase. Likely cause (unverified): KWin reports a present
  up to about a refresh late, which fits a 16.7 ms window but not an 8.3 ms one. Vulkan FIFO has no "hold for n
  refreshes" (D3D11 has `Present(n)`). Next attempt only as a measured spike on `VK_EXT_present_timing`, which
  reports actual display times (available on RADV 26.2; KWin 6.7.5 advertises no commit-timing protocol, so
  target times probably aren't). For stable 30 fps NDI, use "Match Display" and the sender's frame rate (every 4th
  frame at 120 Hz).
- **Visual clock made constant — done (uncommitted).** `AdvanceVisualClock` returns exactly refresh × refreshes per
  frame and steps only in whole *refreshes* (not `n × refresh`) when the wall clock is a refresh ahead or behind; the
  2 % pull is gone. Measured at 120 Hz: "Match Display" 300 of 300 frames at 8.333 ms; the timer-held lock 30
  reports 33.333 with ~20 % 25.0 (frames truly shown 3 refreshes), instead of the old 0-delta frames.
- **Idle external-display hitches vanish under load.** Probably GPU power state or the compositor, not TiXL timing.
- **Offer rates the display divides.** Build the dropdown from `refresh / n` (120 Hz: 120, 60, 40, 30, 24), not a fixed
  list. A heavy scene at 19.6 ms then has 40 fps as an even option.
- **Sustained under-rate is common, not an edge case.** With §3.1 as written ("vsync, no lock → refresh period"),
  the 18 ms load case would run integrators at 8.3 / 20 ≈ 42 % speed. Open question 2 now needs an explicit answer:
  either accept slow motion until the user locks, or make "Match display" choose the display-divided rate the loop
  holds. The second is a heuristic, which §3 otherwise avoids.

## 3. Design

### 3.1 One value

`FrameTiming.FrameDurationSec`, set once per frame by the host (Editor, Player) before evaluation:

| State | `FrameDurationSec` |
|---|---|
| Export / visual test (`FixedFrameDurationSec` set) | the fixed value — unchanged behaviour |
| Vsync, frame-rate lock set | `RefreshPeriodSec * VblanksPerFrame` (lock rounded to whole vblanks, as today) |
| Vsync, no lock | `RefreshPeriodSec` from the SDL display mode of the pacing window |
| No vsync, lock set | `1 / lock`; the loop sleeps to the lock |
| No vsync, no lock | average interval over ~1 s — the only measured input, documented as "unpaced, no guarantees" |

Under vsync it does not change unless the display mode, the pacing display or the lock changes.

### 3.2 Who uses what

Rule: **anything that animates or integrates uses `FrameDurationSec`. Anything that measures real elapsed time
(tooltip timers, input velocity from mouse deltas, metrics) uses the wall clock.**

- `Playback.LastFrameDuration` returns `FrameDurationSec` live (kept as the public name for ops and graphs).
- `Playback.FrameSpeedFactor` = `FrameDurationSec * 60` live — constant, so `.t3` graphs keep working.
- Motion blur: `GetShutterScale` returns `shutter`; velocity is already displacement over one constant step.
- UI damping (`MathUtils.BlendForFrame(..., FrameTiming.VisualDeltaSec)` sites) switches to `FrameDurationSec`.
- `CameraInteraction`, `GamepadCamera`, `ScalableCanvas` damping switch from `ImGui DeltaTime` to
  `FrameDurationSec`. `MagItemMovement` drag speed, `EdgeScrolling`, `OperatorHelp`/`SnixlWindow` timers keep
  the wall clock (they measure real time).

### 3.3 Timeline and beat clock

The timeline is already the master clock (`SoundtrackClipStream` reseeks audio to it), so it must not drift from
wall time, but it must also not inherit CPU jitter.

- `FrameTiming.FrameTimeSec` (replaces `VisualFrameTimeSec`) advances by `FrameDurationSec` per frame.
- It is compared with the wall clock once per frame. If it is behind by **one whole frame duration or more**,
  it jumps forward by whole frame durations (a real drop: the timeline jumps, nothing is smoothed). If it is
  ahead by a whole frame, it holds one frame. No continuous pull.
- After a stall longer than `MaxPlausibleIntervalSec` (breakpoint, minimize, load) it re-anchors to the wall
  clock without catching up.
- `Playback.Update` and `BeatTimingPlayback.Update` advance `TimeInBars` / beat time by the frame's
  `FrameTimeSec` delta (normally exactly `FrameDurationSec`, a multiple on a drop). `BeatTiming.Update` and
  `NdiSender` read `FrameTimeSec`.
- A slightly-off reported rate (59.95 vs 59.94 Hz) produces one single-frame jump roughly every 100 s. Accepted:
  it is honest and visible in the drop counter, and it replaces the 2 % pull.

Note: integrators still step by `FrameDurationSec` on a drop frame, not by the jump. Damping lags by one frame on
a drop, which is the "drop is a drop" behaviour we want.

### 3.4 Measurement (display only, never fed back)

- Keep: raw frame interval, CPU time, GPU time, present duration in `PerformanceMetrics`.
- Add: **dropped-frame counter** — frames whose wall interval exceeds `1.5 * FrameDurationSec`, plus the
  timeline jumps from §3.3. Shown in the metrics bar and Performance window, exposed by the bridge.
- Later, optional: count drops exactly from Vulkan `present_wait`/`present_id` (already enabled in
  `Graphics.Vulkan/VulkanBackend.cs`). Metrics only.
- Optional UI hint (not a timing heuristic): when drops persist, the Performance window suggests locking to the
  next lower rate.

### 3.5 Frame-rate lock

Unchanged UI (`PerformanceWindow` dropdown `[0, 144, 120, 60, 50, 30]`, `UserSettings.Config.TargetFrameRate`,
bridge `setTargetFrameRate`). It becomes the documented answer for projects that cannot hold the display rate
and for VRR displays. The Player gets the same setting if it does not have one yet.

### 3.6 Output-rate override (e.g. stable 30 fps NDI from a 120 Hz laptop)

A real use case: the editor display runs at 120 Hz, but an output (NDI, Spout, a capture device) must receive a
stable 30 fps. This is an explicit override set by the user, not something detected. Two ways, both kept:

1. **Lock the frame rate to the output rate (recommended when the display rate divides evenly).** Lock 30 on
   120 Hz → 4 vblanks per frame, `FrameDurationSec = 1/30`. Every rendered frame is sent, sims and damping step
   33.3 ms, and velocity buffers / motion blur cover exactly the interval the receiver sees. The UI also runs
   at 30.
2. **Render at the display rate, decimate in the sender.** `NdiSender.FrameRate` keeps choosing which frames to
   send, now from `FrameTimeSec`. Because steps are constant, 30 from 120 is exactly every 4th frame with no
   alternation. The UI stays at 120. Downside: each sent frame's motion blur and velocity cover 8.3 ms, not
   33.3 ms, and 3 of 4 frames are rendered for nothing.

**Rates the display can't divide evenly.** 30 fps on 144 Hz is 4.8 vblanks. Today
`ProgramWindows.GetVblanksPerFrame` rounds to 5 and clamps to `MaxVblanksPerFrame = 4`, so "lock 30" gives
**36 fps** — a bug to fix in Phase 1. Options, to be decided:

- Pace the loop by a timer at exactly 1/lock with vsync off for the main window (the UI may tear; the output
  gets exact steps). `FrameDurationSec = 1/lock`. This is the "No vsync, lock set" row in §3.1.
- Or keep vsync and refuse lock rates that aren't whole vblanks of the current display, showing the nearest
  achievable rates in the dropdown.

In every mode `FrameDurationSec` is the lock or output step, never a measured value. NDI timecodes stay
synthesized from the declared rate, so receivers see a clean 30 fps sequence either way. Only the content's
cadence differs between the modes.

## 4. Phases

### Phase 0 — Measure before deleting

Using the debug bridge (`getMetrics`: `frameDeltaSeconds`, `framePeriodMs`, `visualDeltaMs`, `vblanksPerFrame`,
`frameSpeedFactor`), log ~10 s per case on Linux/Vulkan (and Windows if available):

1. Light scene at display rate, vsync on, 60 Hz and 120 Hz.
2. Heavy scene below display rate (target ~40–90 fps on 120 Hz).
3. Scene with motion blur and a constant-speed object; log `GetShutterScale` per frame (injected probe).
4. Lock 60 on a 120 Hz display.

Confirm or reject each hypothesis in §2 and add a "Measured" section to this plan. If a hypothesis is wrong,
revise §3 before Phase 1.

### Phase 1 — Core value and clock

- `Core/Stats/FrameTiming.cs`: add `FrameDurationSec`, `FrameTimeSec`, drop counter; delete
  `AdvanceVisualClock`, `_visualClockLagSec`, `LagCorrectionRate`, `VisualDeltaSec`, `VisualFrameTimeSec`,
  `FrameRateRelativeTo60`. Keep `FramePeriodSec` only for the no-vsync row and metrics (or rename to make that
  explicit).
- `Core/Animation/Playback.cs`: `LastFrameDuration` / `FrameSpeedFactor` per §3.2; timeline advances by the
  `FrameTimeSec` delta; delete `GetLiveFrameDuration` and `_lastUsedTimingFrame`.
- `Editor/Gui/Interaction/Timing/BeatTimingPlayback.cs`: drop its own `_lastFrameStart`.
- `ProgramWindows.GetVblanksPerFrame`: fix the clamp that turns "lock 30" on 144 Hz into 36 fps (§3.6).
- Hosts: `T3Metrics.RecordFrameInterval` and `Player/Program.RenderLoop.cs` call one entry point with
  (refresh rate, is vsynced, lock, wall time).
- Unit tests in `Core.Tests` for the table in §3.1 and the jump rules in §3.3 (fake wall clock).

### Phase 2 — Consumers

- Motion blur `GetShutterScale`, `RepeatFrame`, `TimeConstBuffer` (no code change beyond the source value).
- UI sites listed in §3.2; `NdiSender`, `BeatTiming`.
- Grep gate: no `VisualDeltaSec`, `VisualFrameTimeSec`, `FrameRateRelativeTo60` left; `ImGui.GetIO().DeltaTime`
  only at wall-clock sites.

### Phase 3 — Metrics and lock hint

- Drop counter in metrics bar, Performance window and bridge `getMetrics`; remove `visualDeltaMs`, add
  `frameDurationMs`, `droppedFrames`.
- Optional lock suggestion.
- Manual test set in `.tests-manual/` (frame timing: vsync 60/120 Hz, lock, heavy scene drops visible, motion
  blur steady, beat sync steady).

### Phase 4 — Optional

- Exact drop counting via `present_wait`.

## 5. Open Questions

1. **No vsync, no lock:** is a 1 s average acceptable for integrators, or should that mode default to a lock
   (e.g. the display rate) so there is no measured input at all?
2. **Sustained under-rate:** with this design a project at a steady 45 fps on 60 Hz runs particles and damping
   at 75 % speed while the timeline keeps real time. Confirm that is wanted (the lock is the fix), versus
   switching `FrameDurationSec` automatically — which would bring estimation back.
3. **Window moved to another display mid-run:** `FrameDurationSec` changes on that frame; damping state is fine,
   but velocity buffers see one mixed frame. Accept?
4. **Lock rates the display can't divide evenly** (30 on 144 Hz): timer pacing with vsync off for the main
   window, or only offer whole-vblank rates? See §3.6.
5. **Player parity:** does the Player need the lock dropdown, or a command-line / export setting?

## 6. Risks

- Graphs tuned against the current averaged `FrameSpeedFactor` on high-refresh displays see no change at the
  display rate, but behave differently under drops. Check the 6 library graphs that read it.
- Removing the 2 % pull makes rare single-frame timeline jumps visible with off-by-a-little reported rates.
  The drop counter makes them diagnosable.
