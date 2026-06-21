# Continuous Capture (open-ended video recording)

**Status:** Phase 1 implemented — 2026-06-21. Adds a fourth render *range* mode that records open-endedly
until the user stops it, instead of rendering a fixed `[start, end]` range. Phase 2 (VFR) and realtime audio
remain open. Manual test set: [`continuous-capture.md`](../../.tests-manual/continuous-capture.md).

**Confirmed decisions (2026-06-21):**
- Both clock models (`Realtime`, `Deterministic`) ship in Phase 1, at **Fixed target FPS**.
- **VFR deferred to Phase 2.** The `Variable` enum value exists but is disabled in the UI with a "coming
  soon" hint, so the settings shape is stable and no later migration is needed.
- **Realtime grab is video-only in Phase 1.** Audio export is disabled/greyed while the Realtime clock is
  selected; Deterministic open-ended still records audio normally. Live audio-sync is a follow-up slice.
- Non-nvenc: warn but allow.
- Realtime grab captures at the **native output resolution** in Phase 1 (Resolution % is greyed with a hint);
  the live-texture readback path scales poorly otherwise.

## Goal

A new **Continuous** range mode (alongside `Custom` / `Loop` / `Soundtrack`). When it's selected, pressing the
capture button (or `RenderAnimation` shortcut) **starts recording immediately** and keeps going until the user
presses capture **again**. There is no predetermined end; the second press finalizes the file as a success
(not a cancel/discard).

Motivating use case: live / VJ performance capture (audio-reactive, MIDI, SpaceMouse) where the duration isn't
known up front and NVENC is fast enough to encode in realtime. Adjacent to the microscope-VJ work.

## Two capture models — both are user options

The user confirmed both models are valid and should be selectable in render settings:

| Model | Drives playback? | Output | Best for |
|---|---|---|---|
| **Realtime live grab** | No — leaves `Playback` under live/user control | Grabs whatever the output shows each editor frame, paced to the target FPS | Live performance, interaction, audio-reactive |
| **Deterministic open-ended** | Yes — steps playback forward at target FPS like a normal render | Frame-perfect even if encoding is slower than realtime | Endless generative animation |

New setting: `ContinuousCaptureClock { Realtime, Deterministic }` (default **Realtime** — matches the "nvenc
fast enough for realtime" motivation).

## Two frame-rate modes — both options, but VFR is a later phase

| Mode | Meaning | Cost |
|---|---|---|
| **Fixed target FPS** | Write at settings' FPS; in realtime grab, pace via wall-clock accumulator (skip/duplicate) | Reuses the existing writer path |
| **Actual render rate (VFR)** | Timestamp each grab by wall-clock; encoder writes variable-frame-rate | **Needs API change** |

New setting: `ContinuousFrameRateMode { FixedFps, Variable }` (default **FixedFps**).

**VFR is not free.** `IVideoFileWriter.AddVideoFrame(rgbaPixels, rowStride)` (Core/Video/VideoExport.cs:107)
has no per-frame timestamp/PTS. VFR requires:
- An additional optional PTS argument (or overload) on `AddVideoFrame`, kept back-compat for existing callers.
- The FFmpeg video assembly setting per-frame PTS and a VFR-capable stream timebase.
- Players/editors handle VFR unevenly — document the caveat in the UI.

➡️ **Proposed phasing:** ship Fixed-FPS for both clock models first (Phase 1). Add VFR (Phase 2) once the
writer-API extension is agreed. The `Variable` enum value can exist from day one but be disabled in the
dropdown with a "coming soon" hint, so the format/settings shape is stable.

## Loop changes (`RenderProcess` / `RenderTiming`)

The current loop assumes a known `FrameCount` (RenderProcess.cs:99, completion at :319, `Progress` at :41,
time-stepping via lerp(start,end,progress) in RenderTiming.SetPlaybackTimeForFrame:131-133). Continuous breaks
all three. Plan:

1. **No completion-by-count.** When `Settings.TimeRange == Continuous`, the `currentFrame >= FrameCount` check
   never ends the session; only an explicit stop does. `FrameCount` is left 0 / "unknown".
2. **`Progress` becomes indeterminate.** Returns a sentinel (e.g. `-1` or a separate `IsIndeterminate` flag)
   so the footer draws an activity indicator rather than a 0..1 bar.
3. **Time-stepping:**
   - *Deterministic:* keep forcing `PlaybackSpeed`/`IsRenderingToFile`, but step
     `TimeInSecs = startSecs + FrameIndex / fps` (no lerp; no end clamp).
   - *Realtime:* do **not** force `PlaybackSpeed` / `IsRenderingToFile`. Leave `Playback` as the user left it
     (playing, scrubbing, live). Each editor frame, grab `MainOutputTexture` and feed the encoder.
4. **Pacing (realtime + FixedFps):** wall-clock accumulator. Track capture start time; compute
   `framesDue = floor((now - start) * fps)`; emit (duplicate or drop) to keep the file at constant FPS.
   Log dropped/duplicated counts behind the existing render-profiling toggle, not per frame.

## Stop semantics — finalize, not cancel

Second capture press = **normal finish**: dispose the writer (which calls `IVideoFileWriter.Finish()` →
finalizes the container, FfmpegVideoExportWriter.Dispose:87) and report success
("Captured N s to …"), then auto-increment the version like a normal render. This differs from `Cancel`,
which currently messages as "cancelled". Introduce a `StopContinuous()` (or a `success`/`finalize` flag on the
existing teardown) so the two read differently in the footer and log. `HandleRenderShortCuts`
(RenderProcess.cs:429) routes the second press to stop-as-success when in continuous mode.

## Non-nvenc handling — warn but allow

Confirmed: warn, don't block. When `TimeRange == Continuous`:
- In **Format & Quality**, if `VideoEncoderAvailabilityCache.Get(codec).Kind != Hardware`, draw a
  `StatusAttention` hint: software encoders may not keep realtime pace (frames dropped/duplicated), and some
  containers finalize awkwardly on an open-ended stop. Reuse the existing `DrawInlineEncoderHint` style
  (RenderWindow.cs:412).
- The capture button stays enabled (no new `ValidateSettings` block for this case).

## UI changes (`RenderWindow`)

- **Range segmented button** (RenderWindow.cs:250) gains the `Continuous` option automatically from the enum.
- When `Continuous` is selected:
  - **Source section:** hide/disable Start/End/duration rows (no fixed range). Show the two new dropdowns
    (`ContinuousCaptureClock`, `ContinuousFrameRateMode`) and a short explainer hint.
  - **Footer summary** (`BuildSummaryLine`, RenderWindow.cs:215): no duration/size estimate (unknown). Show
    e.g. "Continuous · 1920×1080 · H.264 · realtime".
  - **Progress footer** (`DrawExportProgressFooter`, RenderWindow.cs:182): replace the 0..1 `ProgressBar` with
    an **activity indicator** — the same 4px line with a ~30%-wide segment scrolling left→right continuously,
    driven by the shared global blink/time source (no per-element timer). Show elapsed time + captured frame
    count instead of "time remaining". The Cancel button becomes **Stop** (finalize-as-success); optionally
    keep a separate discard/cancel affordance — *open question*.

## Settings / interface-stability audit

- `ContinuousCaptureClock` and `ContinuousFrameRateMode` are string-enum JSON fields on `RenderSettings`
  (like the existing `TimeRange`), added to `CopyFrom`. Additive — old `.t3ui` files default cleanly.
- `Continuous` appended to `TimeRanges` (append, don't reorder — `StringEnumConverter` keys by name so order is
  safe, but keep it last for readers scanning the enum).
- Keep `Variable` reserved-but-disabled so enabling VFR later needs no migration.

## Phasing

- **Phase 1 (this feature):** `Continuous` mode; both clock models; Fixed-FPS; stop-as-success; non-nvenc
  warning; activity indicator; source/footer UI. Realtime audio capture scoped below.
- **Phase 2:** VFR (`AddVideoFrame` PTS extension + video-assembly support + player-caveat hint).

## Open questions / risks

1. **Realtime audio capture.** Offline export pulls a deterministic mixdown
   (`AudioRendering.GetFullMixDownBuffer`). Realtime grab needs the *live* output audio, sample-synced to the
   grabbed video frames — this is the hardest part and may warrant its own sub-phase (video-only realtime
   capture first, audio second). **Needs a decision before Phase 1 coding.**
2. **Stop vs discard.** Does the user want only "Stop & keep", or also a separate "Discard"? (Leaning:
   Stop=keep is the primary button; Esc/secondary = discard.)
3. **Resolution stability.** Realtime grab uses the live output texture, which can change size mid-capture
   (window resize). Lock resolution at capture start and skip/letterbox mismatched frames, or stop on change?
4. **Very long captures.** MP4 (32-bit box sizes) caps near ~4 GB unless `faststart`/fragmented MP4; a
   multi-hour VJ set could exceed it. Worth a documented limit or a fragmented-MP4 option later.

## Manual test + help

- Add a `.tests-manual/` set covering: select Continuous, start, perform live, stop, verify a playable file;
  repeat with a software codec to see the warning; both clock models.
- Add a `.help/` page under `using/` for continuous capture once behavior settles.
