# Video Audio Playback (FFmpeg → BASS → audio graph)

**Status:** Phase 1 in progress. Rewritten 2026-08-09 against the audio processing graph
([`Plan_AudioProcessingGraph.md`](Plan_AudioProcessingGraph.md)), which landed after this plan was first
written and changes the *routing* half of it substantially. Extends the FFmpeg video work
([`Plan_FfmpegVideo.md`](archive/Plan_FfmpegVideo.md), [`Plan_VideoZeroCopyDecode.md`](archive/Plan_VideoZeroCopyDecode.md)),
which decode the **video** track but leave **audio silent** — the `Volume` inputs on `[PlayVideo]` /
`[VideoClip]` are no-op placeholders (see the "Audio is silent in this milestone" comments in
`PlayVideo.cs` / `VideoClip.cs`).

## Goal

Decode a video file's audio track with FFmpeg and expose it as an ordinary **audio-graph source**, so that:

1. It plays **in sync with the timeline** (the timeline is master; audio follows).
2. `Volume` finally does something.
3. It routes, groups, mixes, ducks and takes FX inserts through `[AudioBus]` / `[CombineAudio]` /
   `[AudioReverb]` / `[DuckAudioLevel]` — the same machinery every other audio source uses, with no
   video-specific plumbing.
4. It is captured **deterministically** during render-to-file.

**Non-goals (initially):** pitch-correct variable-speed audio, audio scrubbing, multi-track / channel
selection, surround downmix choices. Phase 4 / deferred.

## What the audio graph changes (read this before the old sections)

The original plan predates `AudioGraphNode`. It designed a bespoke path: a new `AudioEngine.UseVideoAudio`
entry point, a per-op stale token, and a hard-coded mixer choice (`[PlayVideo]` → `OperatorMixer`,
`[VideoClip]` → `SoundtrackMixer`). **All of that is superseded.** The graph already solves routing,
lifecycle, transport gating, group gain, FX and metering — the video side only has to produce **one BASS
channel handle** and hand it to a node.

Locked-in decisions:

- **D1 — Second output, `Slot<AudioGraphNode>`.** `[VideoClip]` and `[PlayVideo]` each gain an
  `AudioReference` output and implement `IAudioSource` (`Core/Audio/IAudioSource.cs`). Unwired → the
  implicit default bus (`AudioGraphCollector.CollectLooseSources`); wired → the `[AudioBus]` owns
  membership and gain. One path, no mixer choice baked into the op. **Keep `Texture` as the *first*
  output** — it is the default connection target; `AudioReference` is appended second.
- **D2 — `Volume` becomes the node's `Gain`, not a stream attribute.** `node.Gain = Volume` (folded by
  combinator gains during collection, applied by the realiser). Matches `[AudioClip]` and
  `[AudioToneGenerator]`.
- **D3 — `ExternallyManagedChannel = true`.** The decode side owns the push stream's lifetime and its
  feed position; the graph owns membership + gain only. So the bus routes it **without**
  `MixerChanBuffer` and never frees it. Known consequence (same as `[AudioClip]`): an `[AudioLevel]`
  side-branch tap can't meter it — meter the bus, or wire the tap inline so it realises its own submix.
- **D4 — The *texture* path owns time and feeding; the audio output only publishes channel + gain.**
  A bus evaluating `AudioReference` supplies an `EvaluationContext` whose `LocalTime` is **not** remapped
  through the `TimeClip` (that remap happens inside `TimeClipSlot`), so it cannot drive the feeder.
  Rule: **drawn = audible.** A `[VideoClip]` no `[VideoClipPlayer]` evaluates is silent — which is right;
  a video with no picture should have no sound. Unlike `[AudioClip]`, no bus-as-heartbeat is needed.
- **D5 — Preroll must stay silent.** `_ProcessVideoClips` pulls `Upcoming` clips ~0.5 s before their cut
  to warm the decoder. Gate the feeder on `Active`, not on "was evaluated this frame".
- **D6 — A separate audio decode session, on the *original* path.** See below — this is the biggest
  departure from the first draft.

## D6 in full: why audio gets its own demuxer

The first draft had the audio decode ride along inside `VideoDecoderSession` / `VideoPlaybackController`,
flushing its queue on every `SeekToKeyframeBefore`. Re-reading the controller kills that:

- `ProcessLatestRequest` **returns early on a cache hit** and when the target hasn't changed. During
  smooth playback (and every scrub back into cached territory) no demuxing happens at all — audio would
  starve exactly when playback is going well.
- The zero-copy path has no `VideoFrameCache` and hands GPU surfaces across threads under `_lock`;
  interleaving an audio queue into that handshake adds risk for no gain.
- **Proxies have no audio track** (`ProxyTranscoder` sets `ExportAudio = false`), and
  `VideoPlaybackEngine.ResolveEffectivePath` silently substitutes a proxy for preview. Coupled audio
  would be silent whenever proxy preview is on — a confusing, invisible failure.

So: **`AudioDecoderSession`** is its own `FormatContext` + audio `CodecContext` + `SwrContext`, opened on
the **original** path (never the proxy), with the video stream set to `AVDISCARD_ALL`. It owns its own
seek and flush, driven by the requested playhead. Cost: the file is opened twice and its bytes read twice
(the OS page cache absorbs most of that, since both readers walk the same offsets); video packets are
discarded at the demuxer, so there is no second decode. Worth it for a path that behaves the same under
caching, zero-copy, proxies and export.

## Architecture

### Clock model
The timeline (`Playback.TimeInBars` → `SecondsFromBars`) is master; audio **follows**. This is the opposite
of a normal media player. The video path already maps timeline → source seconds (`[VideoClip]` clamps into
`SourceRange`, `TimeToFrameMapper.ResolvePlaybackSeconds` loops/clamps); audio is handed the **same mapped
seconds**, so a frame and its audio share one clock.

### Project boundaries (BASS in Core, FFmpeg in VideoServices)
- **VideoServices** (FFmpeg side) owns `AudioDecoderSession` and the feeder that drives it.
- **Core/Audio** (BASS side) owns `VideoAudioStream` — the push stream. `VideoServices` references `Core`,
  so PCM crosses in the correct direction and BASS stays out of the FFmpeg assembly.
- The channel handle travels back to the operator through a **separate** engine entry point,
  `IVideoPlaybackEngine.RequestAudio(streamId, absolutePath, sourceSeconds, loop) → channel`, not through
  `VideoFrameResult`. Keeping it separate is what lets a caller pull frames while staying silent — which is
  exactly the preroll case (D5) and the export case — and it mirrors D6's decoupling instead of fighting it.
  The op stays an FFmpeg-free Core client either way.

### Push stream ≠ file stream (why a new type)
`OperatorAudioStreamBase` and `SoundtrackClipStream` are **seekable file/decode streams**: created from a
path, seeked by byte position, exported via `ChannelGetData`. A **push stream is not seekable** — it is
controlled entirely by *what is fed and when*. So video audio needs a new `VideoAudioStream`, **parallel
to** (not a subclass of) `OperatorAudioStreamBase`. It reuses the per-stream `Volume` attribute and mixer
membership; it replaces load / seek / position with a *feeder*.

### Why not "just let BASS open the file"
BASS can open some containers directly, which would reuse existing machinery with almost no new code.
**Rejected on codec coverage:** target content (e.g. the Silo / Foundation Web-DLs) is **E-AC-3 / DDP5.1**,
which stock BASS does not decode → silent. FFmpeg already decodes every codec we care about.

### Lifecycle — what the graph already gives us
No new stale-token mechanism (the first draft's step 4 is deleted):

- **Op not evaluated** → `RequestFrame` stops being called → the engine's `IdleTimeoutMs` eviction disposes
  the controller, and the feeder stops. Feeding also stops immediately when the op stops requesting, so
  the push stream underruns to silence within a buffer length.
- **Bus not evaluated** → `AudioBusRegistry` pauses its submix (2-frame slack).
- **Transport stopped** → `PlaybackSpeed == 0` pauses explicit buses and the implicit default bus.
- **Op disposed** → `ReleaseStream(streamId)` already runs in `Dispose`; it frees the audio stream too.

## The feeder (the load-bearing new work)

A push stream plays whatever it is fed, at 48 kHz wall-clock. Sync = keep the push buffer holding
~50–100 ms of audio starting at the current playhead.

- **Forward 1× play:** decode audio in PTS order from the playhead; top the push buffer up to the target
  fill. BASS plays it out in wall-clock, which equals the timeline at 1×.
- **Drift correction:** compare fed-audio position against the requested time. Small drift rides
  (imperceptible); a jump (seek/scrub) → **flush and refill** from the new playhead.
- **Pause / out of range / preroll:** stop feeding; the buffer drains to silence.
- **Scrub:** flush and mute. (Audio scrubbing is rarely wanted; revisit with grain playback.)
- **Speed ≠ 1×:** mute initially — a raw push would play at the wrong pitch. Later: `atempo` or BASS_FX.
- **Underrun:** brief silence. Acceptable; audio decode is far lighter than the already-realtime video decode.

## Export

Render-to-file is a **non-realtime, per-frame mixdown** (`AudioRendering.GetFullMixDownBuffer`, called per
export frame), and the encoder already muxes whatever that returns — so nothing changes on the encoder side.
Two things are needed:

1. **Deterministic time-sliced feeding.** For each export frame's slice `[t, t+frameDur]` the feeder must
   supply *exactly* that slice's PCM instead of buffering ahead. This is the distinct export feeding mode.
2. **Nothing else** — a graph-routed source reaches the export mixdown through its bus, which the export
   path already force-evaluates (`AudioRendering.PrepareRecording` snapshots live buses). That was the
   hard part and it is already solved for `[AudioClip]`.

## Phases

1. **Decode + resample (VideoServices only).** `AudioDecoderSession`: open the best audio stream, discard
   video, resample to interleaved float / 48 kHz / stereo, expose `HasAudio`, `SeekTo(seconds)`,
   `TryDecodeChunk(out pcm, out startSeconds)`, `Flush()`.
   *Verify: unit tests — round-trip a `VideoFileEncoder`-produced sine through the session and assert
   format, chunk continuity and frequency; the checked-in samples (no audio track) report `HasAudio` false.*
2. ✅ **Push stream + feeder** (landed 2026-08-09, unverified by ear — nothing calls it until Phase 3).
   `Core/Audio/VideoAudioStream` — a decode-mode BASS push stream that never joins a mixer itself (the graph
   places it) and reports `IsInvalidated` off `AudioMixerManager.ResetGeneration`.
   `VideoServices/VideoAudioTrack` — a per-stream worker holding an `AudioDecoderSession` plus the push
   stream, topping the queue up to ~200 ms and re-seeking when the queue's front drifts >120 ms from the
   requested time. `IVideoPlaybackEngine.RequestAudio` plumbed; the engine's stream entry owns the track and
   disposes it on eviction/release.
   **Audibility rule:** the track plays only while the requested time advances forward by 0 < Δ ≤ 0.25 s per
   tick. Stopped, reversed, scrubbed, looped-around and fast-forwarded playback all fail that test and flush
   to silence — which covers "mute on scrub / seek / speed ≠ 1×" with one condition instead of four flags.
   Not calling `RequestAudio` for ~100 ms also mutes, so preroll and export need no extra gating.
   **Two defects found in live test (fixed 2026-08-09):** (a) the worker ticks faster than the operator posts
   requests, so an unchanged time was read as "playhead stopped" and flushed the queue between every pair of
   rendered frames — the step is now judged only when the time actually changes, with a wall-clock timeout for
   a genuine stop; (b) the queue length swings by tens of milliseconds as the mixer pulls in chunks, and
   thresholding it raw at 120 ms resynced ~8×/s on the swing itself — drift is now exponentially smoothed and
   the threshold widened to 300 ms, since steady playback should need no resync at all (the step gate already
   catches every jump). The diagnostic line reports the smoothed `drift`, which is also the measurement needed
   for the constant-offset item below.
   **Adjacent Core fix:** `AudioGraphCollector` never invalidated its static `_defaultBus` on a device change,
   so after switching the audio device the dead handle was reused forever and *all* loose graph audio stayed
   silent until restart — not just video. Now checks `AudioMixerManager.ResetGeneration`, per the rule the
   graph plan already states.
3. ✅ **`[PlayVideo]` in the graph** (landed + **live-tested 2026-08-09**: audible, and
   `PlayVideo → [AudioReverb] → [AudioBus]` applies the insert — video audio is a graph source like any
   other). New `AudioReference` output (`12473a41-5839-4b9b-9c79-2541fe8b630b`, `DirtyFlagTrigger.Always`,
   appended last so `Texture` stays the default connection); implements `IAudioSource` with a no-op
   `EnsureChannelFromStaticInputs` (the channel comes from the evaluated texture path, never from static
   inputs). `Volume` → `node.Gain`; `Volume <= 0` or rendering-to-file requests no audio at all, so a video
   used purely as a texture decodes nothing.
   *Verify: `.tests-manual/video-audio-playback.md`.*
4. ✅ **`[VideoClip]` on the timeline** (landed 2026-08-09, **needs live test**). Same shape as Phase 3:
   `AudioReference` output (`d9da88fb-a5ac-451f-8727-a8ce126432d8`) + `IAudioSource`. The audio request is
   gated on the clip being **active** — the existing `isActive` test (inside `SourceRange` in source time,
   min/max so a reversed clip still gates) was already computed for the export stall and now gates audio too,
   so the player's pre-roll pulls stay silent (D5). Time comes from the same `clampedTime` the frame request
   uses, so picture and sound share one clock.
   *Verify: `.tests-manual/video-audio-playback.md`.*
5. ✅ **Deterministic export** (landed 2026-08-09, **needs live test**). `RequestAudio` gained a
   `renderingToFile` flag; when set, `VideoAudioTrack.RequestForExport` decodes and queues **synchronously on
   the calling thread** before returning, so the mixdown pull that follows sees the right PCM. Nothing in that
   path consults the wall clock: the queue advances exactly one exported frame per call, and the frame's
   duration is taken from the step between two requests (so no render fps has to be threaded through the
   operator API). Re-seeks happen only on a genuine discontinuity — the first frame or a cut — never on drift,
   because a spurious re-seek would make the render non-repeatable. The feed target is one frame's worth: the
   mixdown pulls exactly that per frame, so the queue neither starves nor grows.
   **Threading:** a new `_workLock` guards the session, the push stream and the feed position. The worker keeps
   out of the way for 500 ms after any export request (a timestamp, not a mode flag, so it self-clears), and
   `Dispose` takes the same lock — the evaluation thread can now be inside the track, which the "worker exited,
   so nothing races us" reasoning no longer covers.
   **Adjacent Editor fix:** `AudioGraphCollector.CollectLooseSources` was skipped entirely while rendering, so
   a source not wired into an `[AudioBus]` was decoded and fed but never routed — silent in the rendered file
   while audible live. It now runs during export too. This affected *every* loose graph source (a plain
   `[AudioToneGenerator]` as well), not just video; the collector's own transport gate already exempted
   recording, so it was written expecting this call.
   *Verify: `.tests-manual/video-audio-playback.md` export steps — audio present in the rendered file, wired
   and unwired, and byte-identical across two renders of the same range.*
6. **Polish.** Variable speed (`atempo`), scrub grains, A/V drift telemetry, multi-track / channel
   selection, surround downmix.

## Open questions

- **Back-compat loudness (decided 2026-08-09: leave both at 1.0).** Both ops already default `Volume` to
  **1.0**, so switching audio on makes every existing project's videos audible at once. Accepted — "video
  plays its sound" is the least surprising behaviour and silent-by-default reads as a bug. Needs a release
  note.
- **Proxy regeneration.** Audio always reads the original file, so a proxy that goes stale never affects
  audio — but it does mean proxy preview no longer removes all I/O on the original. Acceptable?
- **Multi-send.** Two buses collecting the same video source steal its channel from each other per frame
  (the documented graph-wide limitation until split streams land). Same for video; no extra work here.
- **Thread/IO cost scales per stream, and that is accepted.** Each audible source adds a feeder thread and a
  second demuxer on top of the video decode thread the engine already spends per stream. At the observed
  concurrency (`VideoPlaybackEngine`: "mostly 0-3 streams, rarely >7") that is a handful of threads and a
  handful of extra read passes — not worth a shared feeder pool or decoder dedup. Note that `_streamId` is
  per op instance, so two clips of the same file at different times are correctly independent; dedup would
  have to key on (file, time) and would essentially never hit. Revisit only if a real project shows dozens of
  simultaneously *audible* videos. Cheap mitigation that is worth doing anyway: a source with `Volume <= 0`
  requests no audio at all, so a video used purely as a texture costs nothing.
- **Fixed A/V offset from mixer-side buffering.** The feeder measures what is audible as
  *fed − queued*, which accounts for the push queue but not for the buffering the mixer adds when the graph
  routes the channel (`AudioGraphCollector` adds loose sources with `MixerChanBuffer`, and BASS's own device
  buffer sits below that). The result is a small *constant* lateness — below the resync threshold, so it never
  churns, but it is never corrected either. Measure it once audio is audible and fold it into the resync
  target as an offset, next to the existing `AudioSyncingOffset = -2/60 s`. Phase 4 tuning.
- **Loop seams.** A loop wrap fails the forward-step test, so it mutes for a tick and then re-seeks: an
  audible gap at every wrap of a looping `[PlayVideo]`. Fixable by unwrapping the step against the duration
  and pre-decoding across the seam. Phase 6.
- **Stale-channel invalidation (fixed 2026-08-09, found in live test).** `node.SourceChannel` was only ever
  written by the *texture* path. When an operator stopped being evaluated — a `[VideoClip]` well outside its
  cut, a disconnected `[PlayVideo]` — the engine evicted its stream after the 5 s idle timeout and freed the
  channel, but the node kept advertising the dead handle. The routing bus evaluates `AudioReference` every
  frame (`DirtyFlagTrigger.Always`), so it retried a freed handle forever: `[AudioBus] failed to route channel
  <negative handle>: Handle`, once per frame, permanently. The per-frame reconciliation can't self-heal this —
  nothing else ever clears the field. Both ops now stamp the frame in `UpdateAudioTrack` and clear
  `SourceChannel` from the reference path after a 2-frame gap, which is the same "drawn = audible" rule the
  feeder already enforces, applied to the wire value rather than the feed.
- **Audio-only use is not supported, by design (confirmed in live test, accepted by the user).** Wiring just
  `AudioReference` into a bus, without the `Texture` output going anywhere, is silent — the texture path is
  what drives the feeder (D4), so an unevaluated op requests nothing. For `[VideoClip]` this is structural:
  the timeline→source remap lives inside `TimeClipSlot` and only happens on the texture output. For
  `[PlayVideo]` it could be lifted (it has no TimeClip; the reference path could compute its own time), but
  only correctly at the top level — inside a time-remapped subgraph the audio path would bypass a remap the
  texture path would have applied. Not worth the asymmetry for now. **Worth doing instead:** an
  `IStatusProvider` warning when `AudioReference` is wired but the op hasn't been evaluated for a few frames,
  so the silence explains itself rather than reading as a bug. Same pattern as the `[AudioLevel]` side-branch
  hint.
- *Resolved in Phase 2:* the resampler targets `AudioConfig.MixerFrequency` (not a hard-coded 48 kHz), and
  the feeder runs on a dedicated worker per stream — the whole point of D6 is decoupling from the video
  worker's cadence.

## Manual test

Add `.tests-manual/video-audio-playback.md` alongside Phase 3 (sync at 1×, routing into an `[AudioBus]`,
group volume + reverb apply, `[AnalyzeAudio]` reacts, silence when the op is removed or the transport stops,
an E-AC-3 clip is audible), and extend it in Phase 4 with clip-range gating and trimming.

## Review handoff (batch of 2026-08-09)

The whole batch is **staged, uncommitted** — `git diff --cached` is the review diff. It spans two plans: the
video-audio work below, plus the `[AudioReaction]` graph tap and the tap helpers on `AudioGraphNode`, which
belong to [`Plan_AudioProcessingGraph.md`](Plan_AudioProcessingGraph.md).

**What is in the batch, by concern:**

| Concern | Files |
|---|---|
| FFmpeg audio decode + resample (new) | `VideoServices/AudioDecoderSession.cs`, `VideoServices.Tests/AudioDecoderSessionTests.cs` |
| BASS push stream + feeder (new) | `Core/Audio/VideoAudioStream.cs`, `VideoServices/VideoAudioTrack.cs` |
| Engine plumbing | `VideoServices/VideoPlaybackEngine.cs`, `Core/Video/VideoPlayback.cs` (`RequestAudio`) |
| Operators | `Operators/Video/lib/io/video/{PlayVideo,VideoClip}.{cs,t3ui}` |
| Audio-graph tap (belongs to the graph plan) | `Core/DataTypes/AudioGraphNode.cs`, `Core/Audio/ChannelAudioAnalysis.cs`, `Core/Audio/AudioAnalysisContext.cs`, `Operators/Lib/io/audio/{AudioReaction,AudioLevel}.cs`, `AudioReaction.t3ui` |
| Pre-existing bug fixed in passing | `Core/Audio/AudioGraphCollector.cs` (device-change handle invalidation) |
| Docs | `.tests-manual/video-audio-playback.md`, both plans, `.help/release-notes/v4.3.md` |

**Verification status — read this before assuming anything is proven:**

- *Automated (64/64 in `VideoServices.Tests`):* **`AudioDecoderSession` only.** Open / no-audio-track /
  missing-file, a 440 Hz tone round-tripped through encode→decode at 48 kHz, resampling to 44.1 kHz, and seek
  anchoring. **Nothing else in the batch has automated coverage.**
- *Live-tested by hand, 14/14 pass (2026-08-09):* `.tests-manual/video-audio-playback.md` — playback and sync
  over a minute, audio-reactive ops, volume including 0, transport stop, scrub/reverse/2× silence,
  un-evaluating, audio-without-picture, bus routing, reverb + grouping, a silent-track file, timeline clip
  in/out, source trim, two-clip handover, proxy preview.
- *Written but not yet run:* the two newest steps — animated volume, and `[AudioReaction]` following one source.
- *Not verified at all:* **export** (Phase 5 not started — video audio is deliberately silent in rendered files
  today); **device-change rebuild** on both new paths (`VideoAudioStream.IsInvalidated` →
  `DropAfterDeviceChange`, and the new `AudioGraphCollector` reset); the **`[AudioLevel]` refactor** onto the
  node helpers (behaviour-preserving by construction, but that op was working before and is now on new code);
  **`[AudioReaction]` in wired mode**, including whether it works in Live Interactive mode.

**Where the reviewer's attention is best spent** — the places reasoning replaced measurement:

1. **`VideoAudioTrack` threading.** Fields are partitioned into "request state, under `_lock`" and
   "worker-thread only" **by comment, not by structure**. Verify nothing worker-only is touched from `Request`
   or `Channel`, and that `_channel` is the only field genuinely crossing threads.
2. **`Dispose` vs. the worker.** Cancel → `_wake.Set()` → `Join(2 s)` → free BASS handles. If the join ever
   times out, the worker can still touch freed handles. (Same shape as the existing
   `VideoPlaybackController.Dispose`, so it's a precedent question, not a novel one.)
3. **Span lifetime.** `AudioDecoderSession.TryDecodeChunk` hands out a `ReadOnlySpan<float>` into a reused
   buffer, valid only until the next call. Enforced by xmldoc alone.
4. **The FFmpeg interop.** `swr_convert` output sizing via `swr_get_delay` + `av_rescale_rnd`; reading
   `raw->extended_data`; the `swr_free` paths in `TryOpen`'s failure branches (double-free / leak); the
   `swr_init` re-initialisation on seek.
5. **`VideoAudioStream.Dispose` calls `MixerRemoveChannel`** while the graph may still believe it holds that
   channel — confirm the bus's per-frame reconciliation self-heals rather than logging forever.
6. **Widened Core surface.** `AudioAnalysisContext`'s ctor went private → internal; `ChannelAudioAnalysis` and
   four `AudioGraphNode` members are new public API. Check none is broader than its actual callers need.
7. **`[AudioReaction]` back-compat.** ~30 existing instances; the unwired path must behave exactly as before.

**Open measurements / decisions:**

- The steady-state `drift=` value from the gated diagnostics (Settings → "Show Audio Logs"). Expected to be a
  small constant lateness from mixer-side buffering the feeder can't see; once known it becomes a fixed offset
  on the resync target. Unmeasured so far.
- Whether a *wired* `[AudioReaction]` analyses correctly in Live Interactive mode (nothing in the submix path
  consults `AudioSource`, so it should — untested).

## Key files

| Concern | File |
|---|---|
| The wire value (leaf: channel + gain + externally-managed flag) | `Core/DataTypes/AudioGraphNode.cs` |
| Loose-source collection into the implicit default bus | `Core/Audio/AudioGraphCollector.cs`, `IAudioSource.cs` |
| Explicit routing / FX / metering realiser | `Operators/Lib/io/audio/AudioBus.cs` |
| BASS init + the three mixers | `Core/Audio/AudioMixerManager.cs` |
| Deterministic export mixdown | `Core/Audio/AudioRendering.cs` |
| FFmpeg demuxer/decoder (the *video* one — audio gets its own) | `VideoServices/VideoDecoderSession.cs`, `VideoPlaybackController.cs` |
| Proxy substitution (why audio reads the original path) | `VideoServices/VideoPlaybackEngine.cs`, `ProxyTranscoder.cs` |
| Operators that drive it | `Operators/Video/lib/io/video/PlayVideo.cs`, `VideoClip.cs`, `_ProcessVideoClips.cs` |
