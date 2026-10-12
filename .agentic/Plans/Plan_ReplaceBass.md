# Replace BASS with a native miniaudio layer

**Status:** Draft — 2026-10-12. Spike done (`Spikes/MiniaudioSpike/`), nothing in Core changed yet.
Decisions from 2026-10-12 are folded in (§Decisions 6–10).
**Scope:** All audio in Editor, Player and operators on Windows, Linux and macOS. Removes ManagedBass,
BASSmix, BASSFLAC and BASSWASAPI.
**Builds on:** [Plan_AudioProcessingGraph](Plan_AudioProcessingGraph.md) (buses, FX inserts, taps — its wire
shape stays, only the realisation changes), [Plan_CrossPlatformV5](Plan_CrossPlatformV5.md),
[Plan_MacOS](Plan_MacOS.md).

## Why

un4seen confirmed (mail, 2026-10-07): bundling BASS with TiXL is fine, but **users need their own BASS
licence for commercial use** — anyone making money with TiXL, and any company even without income. That rules
out paid VJ gigs and studio work without a €950-per-platform licence, which the BASS licence always said
(the "development system" clause) but nobody acted on. Separately, the DX8 effects TiXL uses
(`[AudioReverb]`, `[AudioEcho]`, `[AudioCompressor]`) only exist on Windows, so Linux and macOS need new DSP
anyway, and a fully open stack lets distro packagers ship TiXL without a proprietary blob.

**Until this lands:** ship Flatpak/AUR with BASS as before, plus a licence notice (docs, About, export dialog
— `PlayerExporter.BassLicenseNote` already exists — and the Flatpak metainfo licence field
`MIT AND LicenseRef-proprietary`). Don't block packaging on this plan.

## Decisions

1. **miniaudio** (public domain / MIT-0, single C file, v0.11.25) with stb_vorbis. Decodes WAV, FLAC, MP3,
   OGG itself; **no FFmpeg for audio**. Video audio keeps coming from the FFmpeg video path, pushed in.
2. **Everything on the audio thread is native.** A small C library (`tixlaudio`, grown from
   `Spikes/MiniaudioSpike/native/tixl_audio.c`) owns mixing, decoding, DSP nodes, taps and device callbacks.
   **No managed code ever runs on an audio or capture thread** — no managed callbacks, not even for capture.
   C# calls blittable P/Invokes from the main thread once per frame: set parameters (atomics), rewire nodes,
   read results.
3. **The engine never owns a device.** A device we open pulls it (so devices switch without rebuilding the
   graph); export pulls a separate offline engine per video frame.
4. **Analysis stays in C#** (FFT, bands, beat tracking — `AudioAnalysisContext`, `Timing/`). Native taps only
   hand over sample history. The spike's `Fft.cs` reproduces BASS `FFT2048` exactly.
5. **One handle type instead of BASS ints** in Core's public API (see §Handle leaks).
6. **Switch completely — no second backend.** No `AudioBackend` setting, no period where BASS and the native
   layer coexist. Work happens on a branch; the switch lands in one release.
7. **The MP3 start offset (529 samples, 12 ms) is accepted** as is. No per-format compensation.
8. **Effects are dropped for the switch.** `[AudioReverb]`, `[AudioEcho]`, `[AudioCompressor]` were
   experimental; they become pass-through (warning via `IStatusProvider`) until native nodes are written.
   The FX-insert wire shape and the bus/submix machinery stay, so effects can come back without graph changes.
9. **Windows-only codecs (AAC/M4A/WMA through Media Foundation) are not supported.** Document the formats.
10. **Export renders the live engine** with its device detached (§Export).

## What the spike proved

Run `dotnet run -c Release --project Spikes/MiniaudioSpike -- all --null --gc-stress`; details in its README.

| Risk | Result |
|---|---|
| GC stalls audio | 1,400–1,800 forced compacting gen2 GCs per run: 0 late callbacks, max gap ≈ one period |
| Per-frame allocations | 0 bytes (bind every P/Invoke once at startup — first call allocates) |
| FFT parity with BASS | identical: sine 0.4998 vs 0.5000; music mean Δ 0.0000 in TiXL's 0..1 dB space |
| Export determinism | bit-identical, 250–430× realtime, gapless start — only with the synchronous decoder |
| Export through the live engine | device detached, sources reset, video audio pushed per frame: bit-identical, live resumes |
| Live rewiring between buses/inserts | click-free at 60 reroutes/s, **if every routing node has pitch disabled** |
| Push streams (video audio) | 0 underruns at a 100 ms target fill; drift control still needed (+53 ms/min) |
| Device switch | 4–18 ms, playback continues, graph untouched |
| PipeWire latency | 16 ms buffer at 256-frame periods, no late callbacks (BASS: 20 ms) |
| Seeking a 10.5 min MP3 | < 0.1 ms with a seek table (but see the seek bug below) |
| Windows build | cross-compiles with Zig, imports only UCRT + KERNEL32; **not run yet** |

## Known problems to solve (found by the spike)

1. **dr_mp3 seek-table seeks land one MP3 frame (1152 samples) late** vs. its own linear decode; brute-force
   seeks are exact. The seek-table path (`ma_dr_mp3_seek_to_pcm_frame__seek_table`) and linear decoding
   disagree about the gapless delay. Fix locally in the vendored header, report upstream, keep the spike's
   `bass` check as the regression test. Without a seek table, seeking a long MP3 means decoding from the
   start — too slow for scrubbing.
2. **MP3 start offset vs. BASS: 529 samples (12 ms at 44.1 kHz)** — the libraries trim the decoder delay
   differently. Accepted (decision 7). The spike's `bass` check keeps reporting it as a failed check; that's
   the known offset, not a regression.
3. **miniaudio's resource-manager streaming is unusable for us**: asynchronous loading rendered 10 s of
   silence offline and builds no MP3 seek table. Always use our own `ma_decoder` data sources.
4. **Pitch adds latency.** `ma_sound_group` with pitch enabled carries a resampler; moving a source between
   paths of different latency jumps its phase. All buses/groups/inserts use `MA_SOUND_FLAG_NO_PITCH`; speed is
   applied on the source (as BASS does with `Frequency`).
5. **A seek kept stale audio** (found while spiking export): sounds that resample read input ahead into a
   processing cache, and a seek neither drops that cache nor resets the resampler, so the old position
   played first — in live scrubbing too. Patched in the vendored `miniaudio.h` (marked "TiXL patch", in the
   seek handling of `ma_engine_node_process_pcm_frames__sound`); report upstream.
6. **Push sources must not resample.** With pitch enabled a push source reads ahead past what was pushed for
   the frame. Push sources run at the engine rate with `MA_SOUND_FLAG_NO_PITCH`.
7. **ALSA and JACK** can't open the device while PipeWire owns it; the PulseAudio backend works. miniaudio 0.11
   has no native PipeWire backend — fine, but list it in the device UI as "PipeWire (via PulseAudio)".

## Behaviours to preserve (from the code survey)

- **Soundtrack sync** (`SoundtrackClipStream.UpdateSoundtrackTime`): the timeline is master; each frame the
  decode position (minus `AudioSyncingOffset = −2/60 s`) is compared with the target and reseeked past
  `AudioResyncThreshold × |speed|` (+ `AudioTriggerDelayOffset`). These constants are tuned to BASS's
  decode-ahead and 100 ms/20 ms buffers — retune against the native position (cursor published per callback,
  extrapolated with a shared monotonic clock; spike drift ≤ one callback).
- **Mixer semantics** the audio graph relies on: a channel lives in exactly one mixer; pause via mixer flag;
  `MixerReset` seeks; per-channel FFT/level needs `MixerChanBuffer`; FX retire fades out 400 ms
  (`ChannelSlideAttribute`) and frees deferred; stale buses pause (`AudioBusRegistry`).
- **FFT**: live = `FFT2048` on the global mix (Hann, channels averaged); input = `FFT2048 | FFTRemoveDC`;
  per-source = on the channel; editor spectrum images = offline seek + FFT. Post-processing in
  `AudioAnalysisContext` (−80..0 dB → 0..1, 32 log bands, peaks/attacks/onsets) is unchanged.
- **Export** (`AudioRendering`): synchronous pulls of all decode sources on the render thread, export mixer
  separate from live output, `ResetForExport` of FX tails, spatial streams rendered manually and summed.
- **Device lifecycle**: Linux device preference order (PipeWire/Pulse over raw cards), mixer rate = device rate,
  the `ResetGeneration` contract, and "audio unavailable → silent but running".
- **Formats**: WAV/AIFF/MP3/OGG/FLAC (AIFF through miniaudio's WAV decoder). AAC/M4A/WMA on
  Windows are dropped (decision 9).

## Export

Today `AudioRendering` pauses the global mixer, moves soundtrack clips into a separate export mixer, pulls the
operator mixer synchronously, renders spatial streams by hand, and pushes ~1024 frames into a temporary
stream for the FFT. With the native layer, export renders **the same engine the editor plays through**:

1. **Detach the device** (`ta_detach_device`). The graph — buses, generators, video push sources, taps —
   stays; nothing is moved into a second mixer, so graph-routed sources export exactly as they're heard.
2. **Reset every source** for a repeatable start: seek file sources to the render start (the patched seek drops
   read-ahead and resampler history), clear push queues, reset generators (`ta_sound_reset_state` for sources
   that aren't seeked). Effects, once they exist, need a tail reset too (today's `ResetForExport`).
3. **Per video frame:** let operators update parameters, push that frame's video audio synchronously
   (`VideoAudioTrack.RequestForExport` already does this), then `ta_render` exactly the frame's samples:
   frame `n` covers `[n·rate·den/num, (n+1)·rate·den/num)` — no per-frame rounding.
4. **Analysis during export** reads the output tap's history like live playback (no zero-padded push stream),
   so exported reactions match live ones.
5. **Re-attach the device** afterwards; playback continues.

Workarounds for what the spike hit, and what's still to decide:

- *miniaudio's own streaming renders silence offline* → never use it; file sources are our synchronous
  `ma_decoder`s (decision in §Known problems 3).
- *Stale audio after a seek* → miniaudio patch (§Known problems 5).
- *Push sources reading ahead* → no pitch on push sources (§Known problems 6).
- *Seek-table offset* (§Known problems 1) → until dr_mp3 is fixed, export can avoid it: export only moves
  forward, so one brute-force seek at the start (~0.5 s for a 10 min MP3) plus linear reads is exact.
  Alternatively decode MP3 soundtracks to memory in the background after loading (≈ 57 MB as 16-bit for
  10 min stereo), which makes every seek exact, live and in export.
- *Export rate differs per machine* → render at a fixed 48 kHz independent of the device.
- *Clip jumps during export* (loops, cuts) → reseek only on real discontinuities, as `VideoAudioTrack` does,
  never on drift — there is no clock to drift against when pulling.
- *Spatial sounds* go through the graph with miniaudio's spatializer, so live and export render the same way
  (today export approximates BASS 3D by hand).

## Fix along the way (bugs the survey found)

- Export rounds each frame's sample count without a remainder: −110 ms / +150 ms drift over 10 min at
  23.976 / 29.97 fps. Fixed by §Export step 3 (spike: 0 drift).
- Export FFT runs on a push stream holding ~1024 frames, likely zero-padded — fixed by §Export step 4.
- The bin-to-band table is `static readonly` from `MixerFrequency` at type load (48 kHz) — stale on 44.1 kHz.
- `AudioInput.OnCapturedData` runs analysis, beat tracking and WAV writing on the capture thread, racing
  the main thread (only band arrays are locked). Native capture → ring buffer → analysis on the main thread.
- Live 3D audio bypasses all mixers (no FFT/metering, export differs from live). Route spatial sounds through
  the graph with miniaudio's spatializer, and render export the same way.
- `AudioDeviceSelector` calls `Bass.Configure(UpdateThreads, false)` (sets 0 threads) and changing the
  *input* tears down the *output*.
- `SoundtrackClipStream` sets `ReverseDirection`, a BASS_FX attribute; BASS_FX isn't loaded, so it's a no-op.
- ManagedBass's DX8 `EffectType` enum is misnumbered (already worked around) — disappears with BASS.

## Handle leaks to replace

BASS `int` handles appear in: `AudioMixerManager.OperatorMixerHandle`,
`AudioEngine.TryGetSoundtrackChannel/TryGetOperatorChannel`, `AudioGraphNode.SourceChannel` and the
`FxInsert` `Action<int>` callbacks, `AudioBusRegistry.MarkAlive(int)`, `ChannelAudioAnalysis.Update(int)`,
`VideoAudioStream.Channel`/`VideoAudioTrack.Channel`, `StreamHandle` inside operators,
`AudioConfig.BassFftDataFlag` (a ManagedBass type), and `AudioInputBackend.GetData(flags)`/packed `GetLevel`.
Operators calling BASS directly: Reverb, Echo, Compressor, ToneGenerator, AudioLevel, AudioBus,
PlaySpatialAudioSample. Editor: AudioImageGenerator, AudioDeviceSelector, AudioClipsToOps,
AudioClipDurationCache, TimelineClipDrop.

## Native API shape

Grown from the spike's `ta_*` functions; keep it flat C with integer handles and blittable structs.

- **Engine:** create, destroy, switch / detach / attach device, render (while detached), info, stats,
  output gain.
- **Sources:** file (own decoder + fixed seek table), sine/ADSR generator (replaces the managed STREAMPROC),
  push (ring buffer, silence on underrun, queue depth for drift control, clear), reset state. Play/pause/seek/pitch/volume/pan,
  3D position. A generation counter per slot so stale handles fail instead of hitting a reused slot.
- **Graph:** buses (groups, no pitch), inserts (empty "tap insert" for metering now; effects later),
  taps (peak, RMS, mono history; read-latest and read-since), connect/rewire, fades on detach. Rewiring is
  queued and applied by the audio thread between blocks.
- **Analysis input:** capture device (+ WASAPI loopback) into a tap ring; the main thread reads it.
- **Offline helpers:** decode peaks (waveform), decode window (spectrum images, BASS comparison), duration.
- Every function is safe to call from the main thread only (except `render` on the export thread);
  the audio thread never calls back.

## Phases

Each phase ends with the editor running on all three platforms and a short manual test in `.tests-manual/`.

### Phase 0 — finish the spike (small)

- Fix and upstream the dr_mp3 seek-table offset; resolve the 529-sample question (problems 1–2).
- Run `tixlaudio.dll` on Windows: WASAPI output, latency, **loopback capture**, device switch. Build on a Mac
  (CoreAudio). Decide how CI builds the natives: Linux `cc`, Windows Zig cross-compile or MSVC, macOS runner;
  artifacts in `Dependencies/<rid>/` like BASS today.
- Report the two miniaudio issues upstream (seek-table offset, stale read-ahead after seek) with the spike's
  reproductions.

### Phase 1 — native library and managed facade in Core

- Move `tixl_audio.c` + vendored miniaudio to a real location (e.g. `Native/TixlAudio/`), build it in CI for
  win-x64, linux-x64, osx-arm64; copy like the BASS natives (Editor.csproj, Player.csproj, installer).
- `Core/Audio/Native/` (or similar): P/Invoke surface plus a thin managed facade with the new handle type.
- No feature flag (decision 6): phases 1–5 happen on one branch, which is merged when the editor and Player
  reach parity; BASS is deleted in the same change.

### Phase 2 — soundtrack, timeline, waveform

- `SoundtrackClipStream` on a native file source; position, speed (pitch), looping, out-of-range pause.
- Retune sync constants against the native position; test with a click track at 60 fps and in the Player.
- Waveform/spectrum images and duration probes on native offline decode (background tasks as today).

### Phase 3 — analysis

- Live FFT from the output tap (or the soundtrack bus, per Plan_AudioProcessingGraph §3), per-source FFT from
  tap inserts, `FFTRemoveDC` for input. Verify with the spike's comparison that every `[AudioReaction]` mode
  produces the same values as BASS on the example projects.
- Capture via native input + ring buffer; move `AudioInput.OnCapturedData` work to the main thread.

### Phase 4 — graph, operators, effects, spatial, video audio

- `AudioBusRouter`/`AudioGraphCollector`/`AudioBusRegistry` realise buses, inserts and taps natively.
- `[AudioReverb]`/`[AudioEcho]`/`[AudioCompressor]` become pass-through with a status warning (decision 8);
  `[AudioLevel]`/`[AudioBus]` levels from taps.
- `[AudioToneGenerator]` as a native generator (ADSR in C), `[PlayAudioSample]`, `[PlaySpatialAudioSample]`
  through the graph with the spatializer.
- `VideoAudioStream` as a push source; keep the drift control on the queue depth.

### Phase 5 — export, Player, removal

- `AudioRendering` as in §Export: detach, reset, per-frame push + render, re-attach.
- Player on the native path.
- Delete ManagedBass packages, `BassLibrary`, the BASS natives,
  `Installer/Linux/fetch-bass.sh`, `BassLicenseNote`, `Dependencies/licenses/BASS-un4seen.txt`; add the
  miniaudio/stb_vorbis notices. Update Flatpak metainfo to plain `MIT`.

## Rough size

The survey counts ~13.9k lines of audio code, ~6.2k of them in Core/Audio files that call ManagedBass;
the analysis math, `Timing/`, ADSR and data models (~3k lines) don't change. Phases 2 and 4 are the bulk.
The largest unknowns are not the API swap but (a) re-tuning soundtrack sync so existing projects stay in sync
and (b) export parity — making every source kind resettable and every video frame's audio repeatable.

## Open questions

- Fixed 48 kHz engine rate with device-side conversion, or follow the device rate as today?
- MP3 seeks until dr_mp3 is fixed: patch the seek-table path ourselves, or decode MP3 soundtracks to memory
  in the background (exact seeks everywhere, ~57 MB per 10 min)?
- Effects later: Freeverb-style reverb and a feed-forward compressor, or none until someone asks?
