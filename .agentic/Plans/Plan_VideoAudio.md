# Video Audio Playback (FFmpeg → BASS)

**Status:** Draft — 2026-06-07. Design only, no code yet. Extends the FFmpeg video work
([`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md), [`Plan_VideoZeroCopyDecode.md`](Plan_VideoZeroCopyDecode.md)),
which decode the **video** track but leave **audio silent** — the `Volume` inputs on `[PlayVideo]` /
`[VideoClip]` are no-op placeholders (see the "Audio is silent in this milestone" comments in
`PlayVideo.cs` / `VideoClip.cs`).

## Goal

Decode a video file's audio track with FFmpeg and route it through TiXL's existing BASS audio engine so that:

1. It plays **in sync with the timeline** (the timeline is the master clock; audio follows).
2. It is summed into `GlobalMixer`, so `[AnalyzeAudio]` / `[AudioFrequencies]` react to it **for free**.
3. It is captured **deterministically** during render-to-file.
4. The `Volume` inputs finally do something.

**Non-goals (initially):** pitch-correct variable-speed audio, audio scrubbing, multi-track / channel selection,
surround downmix choices. These are Phase 4 / deferred.

## Why not "just let BASS open the file"

BASS can open some containers directly (`Bass.CreateStream(path, …)`, the way `[AudioClip]` /
`OperatorAudioStreamBase.TryLoadStreamCore` does), which would reuse all existing machinery with almost no new
code. **Rejected on codec coverage:** the target content (e.g. the Silo / Foundation Web-DLs) is **E-AC-3 /
DDP5.1**, which stock BASS does not decode → silent. FFmpeg already has the file open for video and decodes every
codec. So the chosen approach is: **FFmpeg decodes the audio; we push PCM into a BASS push stream.**

## Architecture

### Clock model
The timeline (`Playback.TimeInBars` → `Playback.SecondsFromBars`, `Core/Animation/Playback.cs`) is master; audio
**follows**. This is the opposite of a normal media player (audio-is-master). The video path already maps timeline
time → source PTS (`TimeToFrameMapper`); audio reuses the same mapped time, so a frame and its audio share one clock.

### Project boundaries (keep BASS in Core, FFmpeg in T3.Video)
- **T3.Video** (FFmpeg side): the decode worker (`VideoPlaybackController` / `VideoDecoderSession`) already owns
  the demuxer. Extend it to also decode the **audio** stream and resample with **libswresample** to interleaved
  **float, 48 kHz, stereo** PCM tagged with PTS. Sdcb.FFmpeg exposes the raw resampler (`SwrContext`,
  `swr_alloc_set_opts2`, `swr_init`, `swr_convert` in `Sdcb.FFmpeg.Raw`).
- **Core/Audio** (BASS side): a new **push-stream** owner that registers on a mixer and the stale token, and is
  fed the PCM. BASS push stream = `Bass.CreateStream(48000, 2, BassFlags.Float, StreamProcedureType.Push)` fed via
  `Bass.StreamPutData(handle, buffer, bytes)`, added to a mixer with `BassMix.MixerAddChannel`.
- PCM crosses **T3.Video → Core** (the correct dependency direction; Core is referenced by T3.Video). The engines
  **cooperate via the PCM hand-off; they are not merged.**

### Push stream ≠ file stream (why a new type)
`OperatorAudioStreamBase` (`Core/Audio/OperatorAudioStreamBase.cs`) and `SoundtrackClipStream` are **seekable
file/decode streams**: created from a path, seeked by byte position (`ChannelSetPosition` / `ChannelSeconds2Bytes`),
speed via frequency, exported via `ChannelGetData`. A **push stream is not seekable** — it is controlled entirely by
*what is fed and when*. So video audio needs a **new `VideoAudioStream`** type, **parallel to** (not a subclass of)
`OperatorAudioStreamBase`. It **reuses**: mixer routing, the `MixerChanPause` stale/pause flag, the per-stream
`Volume` channel attribute, and `BassMix.ChannelGetLevel` metering. It **replaces**: load / seek / position with a
*feeder*.

### What is reused for free
- **Lifecycle — "silence when not updated" (the hen/egg problem, already solved).** The AudioEngine has a per-frame
  keep-alive token (`LastUpdatedFrameId` + `SetStale`, reaped in `AudioEngine.CompleteFrame`). Stale operator
  streams (not touched this frame) are auto-paused. `[PlayVideo]` / `[VideoClip]` call a `UseVideoAudio(id, time,
  volume)` each `Update()`; a clip whose `Update` stops being called (playhead moves off it, op deleted/bypassed)
  is paused on the next frame, exactly as a stale `[AudioPlayer]` is, with an `UnregisterOperator`-equivalent on
  dispose. **No new lifecycle mechanism is invented.**
- **Analysis.** Route free-floating `[PlayVideo]` audio into `OperatorMixer` and timeline `[VideoClip]` audio into
  `SoundtrackMixer` (both defined in `AudioMixerManager`); both feed `GlobalMixer`, off which
  `AudioAnalysisContext.Default` reads its FFT (`AudioEngine.UpdateFftBufferFromSoundtrack`). So `[AnalyzeAudio]` /
  `[AudioFrequencies]` pick up video audio with **zero extra wiring**.

## The hard part: feeding a push stream in sync (the feeder)

A push stream plays whatever it is fed at 48 kHz wall-clock. Sync = keep the push buffer holding ~50–100 ms of
audio starting at the current playhead.

- **Forward 1× play:** the worker decodes audio frames in PTS order from the playhead; the feeder tops the push
  buffer up to the target fill. BASS plays it out in wall-clock, which equals the timeline at 1×. Clean.
- **Drift correction:** compare fed-audio position vs playhead. Small drift rides (imperceptible); larger drift
  (a seek) → **flush the push buffer and refill** from the new playhead.
- **Pause:** stop feeding + `MixerChanPause`. **Resume:** refill from the playhead.
- **Scrub:** flush and **mute** (audio scrubbing is rarely wanted; revisit with grain playback later).
- **Speed ≠ 1×:** initially **mute** (a raw push would play at the wrong pitch). Later: FFmpeg `atempo` or BASS_FX
  tempo.
- **Underrun:** if decode falls behind, BASS underruns → brief silence (acceptable; audio decode is far lighter than
  the already-realtime video decode).

## Export (deterministic)

Render-to-file is a **non-realtime, per-frame mixdown** (`AudioRendering.GetFullMixDownBuffer`, called per export
frame). A live "buffer-ahead" push stream does not fit that directly. For each export frame's slice
`[t, t+frameDur]`, the feeder must supply **exactly that slice's PCM** (decode audio for the slice, push, mix down) —
a distinct, deterministic, time-sliced feeding mode vs the live buffer-ahead mode. This is its own phase.

Once this Phase 3 lands, the video's audio is summed into the export mixdown like any other source, so the
**encode milestone** ([`Plan_FfmpegEncode.md`](Plan_FfmpegEncode.md)) encodes it **for free** — that writer
consumes the same `GetFullMixDownBuffer` PCM the MF path does, and audio never forces the GPL build (native
AAC/FLAC are LGPL). The encoder choice (MF vs FFmpeg) is orthogonal to the audio *routing* designed here.

## Phases

1. **MVP — `[PlayVideo]` audio, forward 1× only.** FFmpeg decodes + resamples the audio track in the existing
   worker; a new `Core/Audio/VideoAudioStream` push stream is registered on `OperatorMixer` and fed; `[PlayVideo]`
   calls `UseVideoAudio` each frame and drives `Volume`. Mute on scrub / seek / speed≠1.
   *Verify: audio plays in sync at 1×; `[AnalyzeAudio]` reacts; stops within a frame when the op is deleted or
   bypassed; E-AC-3 / AAC / AC-3 all produce sound.*
2. **`[VideoClip]` on the timeline.** Route to `SoundtrackMixer`; map timeline→source time via the clip's
   `TimeRange` / `SourceRange` (already computed for the video texture); pause when the playhead is outside the clip
   (mirror `SoundtrackClipStream`'s bounds check).
   *Verify: a video clip plays its audio only within its cut; trimming / scaling maps correctly.*
3. **Deterministic export.** Time-sliced feeding into the export mixdown.
   *Verify: the rendered file's audio is frame-aligned and identical across two renders of the same range.*
4. **Polish.** Variable-speed (`atempo`), scrub grains, A/V drift telemetry, multi-track / channel selection,
   surround downmix.

## Risks / open questions

- **Sync precision** — the feeder's fill target vs perceptible A/V offset needs tuning (the existing
  `AudioSyncingOffset = -2/60 s` hints at the ballpark TiXL already accepts).
- **Push-stream flush semantics** — confirm the exact ManagedBass call to clear a push stream's buffered data on a
  seek (re-create vs position reset vs an `End` marker). Load-bearing for the drift/seek path.
- **Threading** — audio decode + feed on the video worker vs a dedicated audio thread; BASS calls must be safe from
  the calling thread. The AudioEngine is driven from the eval thread today (`CompleteFrame`); decide where the
  per-frame feed tick lives.
- **Resampler cost** — `swr_convert` per audio frame on the worker (cheap, but confirm under the 4-stream case).
- **Multiple concurrent video-audio streams** (the 4-clip stress case) — N push streams on the mixer is fine, but
  feeder CPU and demuxer audio decode add up; the existing `MaxLiveStreams` cap applies.
- **Decision (proposed yes):** free-floating `[PlayVideo]` → `OperatorMixer`, timeline `[VideoClip]` →
  `SoundtrackMixer`, matching the existing export routing split. Alternative: one dedicated video mixer.

## Manual test

Add `.tests-manual/video-audio-playback.md` alongside Phase 1 (sync at 1×, `[AnalyzeAudio]` reacts, stops when the
op is removed, E-AC-3 clip is audible).

## Key files

| Concern | File |
|---|---|
| BASS init + the three mixers (Global / Operator / Soundtrack) | `Core/Audio/AudioMixerManager.cs` |
| Stale token, `CompleteFrame`, operator-audio entry points | `Core/Audio/AudioEngine.cs` |
| File-stream base (the *contrast* — push stream is parallel) | `Core/Audio/OperatorAudioStreamBase.cs` |
| Timeline clip stream + bounds-pause pattern | `Core/Audio/SoundtrackClipStream.cs` |
| FFT analysis off `GlobalMixer` | `Core/Audio/AudioAnalysis.cs`, `AudioAnalysisContext.cs` |
| Deterministic export mixdown | `Core/Audio/AudioRendering.cs` |
| FFmpeg demuxer/decoder to extend for audio | `Video/VideoDecoderSession.cs`, `VideoPlaybackController.cs` |
| Operators that drive it | `Operators/Lib/io/video/PlayVideo.cs`, `VideoClip.cs` |
