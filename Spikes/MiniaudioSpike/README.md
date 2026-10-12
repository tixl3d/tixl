# miniaudio spike

Throwaway proof that miniaudio can replace BASS without a realtime cost. BASS is free only for
non-commercial use and its users need their own licence for paid work; miniaudio is public domain / MIT-0,
so an all-open audio stack would let TiXL ship without that caveat.

## Design under test

Everything that runs on the audio thread is native (`native/tixl_audio.c`, built into `libtixlaudio.so`):
miniaudio's node graph mixes, decodes and spatializes, custom DSP is a native node, and managed code never
runs inside an audio callback, so a garbage collection can't stall it. C# only sets parameters (atomics),
rewires nodes and reads results once per frame through blittable P/Invoke calls (`NativeAudio.cs`).

The engine never owns a device. In realtime mode our own device callback pulls it, so the output device can
be swapped without touching the graph; in offline mode the caller pulls it, which is how export renders.

| BASS feature TiXL uses | Here |
|---|---|
| Decode streams (MP3/OGG/WAV/FLAC) with `Prescan` | `ma_decoder` with an MP3 seek table, played via `ma_sound` |
| Speed via the `Frequency` attribute | `ma_sound_set_pitch` (resampling, pitch follows speed) |
| BASSmix submixes, moving channels between mixers | sound groups as buses, `ta_connect` rewiring |
| `ChannelGetLevel` on a buffered submix | pass-through tap node with peak and history |
| DX8 echo/reverb/compressor (Windows only) | custom native node; the echo stands in for all three |
| `Bass3D` spatial streams | `ma_sound` spatialization + listener |
| `ChannelGetData(FFT2048)` | native ring-buffer tap + managed FFT (`Fft.cs`) |
| Decode without playback (waveform, spectrum images) | `ta_decode_peaks`, `ta_decode_window` |
| `StreamPutData` push streams (video audio) | ring-buffer data source that plays silence on underrun |
| STREAMPROC (tone generator) | `ma_waveform` source, native |
| Export mixer pulled per video frame | offline engine, `ta_render` |
| Record / WASAPI loopback | capture device; loopback on WASAPI, monitor sources on PulseAudio/PipeWire |
| `ChannelGetPosition` | cursor published per callback, extrapolated with a shared monotonic clock |

## Running it

```
dotnet run -c Release --project Spikes/MiniaudioSpike -- [command] [options]

commands:
    basics          playback, seeking, position, FFT tap, allocations per frame (default)
    export          offline rendering: determinism, speed, samples per video frame
    bass            side-by-side with BASS: FFT scale, spectra, sample alignment, seek accuracy
    routing         rewiring a source between buses and an insert every frame, click detection
    push            pushed PCM like video audio: underruns, clicks, clock drift
    latency         buffer sizes granted per Linux backend
    device-switch   reopening the output device while playing
    all

options:
    --null          miniaudio's null backend; runs without a sound device
    --audible       play at half volume (muted by default; taps still see the signal)
    --gc-stress     allocate and force compacting gen2 collections on another thread every 100 ms
    --capture       open the default capture device for a second (basics)
    --seconds N     length of the frame loops (default 12)
    --file <path>   audio file (default: the works-for-everybody example soundtrack, a 10.5 min MP3)
```

The exit code is 0 when every check passes. The native library is compiled by `native/build.sh` as part of
the build on Linux (needs `cc`). `native/build-windows.sh <zig> <dir>` cross-compiles `tixlaudio.dll` with Zig;
macOS needs Apple's SDK headers and has to be built on a Mac.

## What it measured (Arch Linux, Radeon 8060S machine, Release build)

- **GC can't touch the audio thread**: ~1,400–1,800 forced compacting gen2 collections per run, zero late
  callbacks, max callback gap 10–11 ms on a 10 ms period. Callbacks average 50–80 µs (≤ 0.8 % of a core).
- **Zero managed allocations per frame** (position, tap read, FFT, parameter writes). .NET allocates once
  when it binds a P/Invoke stub on first call, so bind all entry points at startup.
- **FFT matches BASS exactly**: calibration sine 0.4998 vs. BASS 0.5000; on music, with the windows aligned,
  scale 1.0002 and a mean difference of 0.0000 in TiXL's normalized (-80..0 dB) space. Hann window, channels
  averaged to mono, magnitudes × 4/N reproduce `FFT2048`. ~25–30 µs per FFT in C#, allocation-free.
- **Export through the live engine**: detaching the device, resetting the sources and pulling the same graph
  per video frame (video audio pushed synchronously before each pull) renders bit-identically twice in a row,
  and live playback resumes after re-attaching.
- **Export**: bit-identical between runs, 250–430× realtime, no gap at the start, but only with the own
  decoder: miniaudio's resource-manager streaming loads asynchronously and rendered 10 s of silence offline.
  Rounding each video frame's sample count (what TiXL does today) drifts −110 ms (44.1 kHz, 23.976 fps) and
  +150 ms (48 kHz, 29.97 fps) over 10 minutes; ranges derived from the frame index drift 0.
- **Rewiring is click-free** at 60 reroutes per second between two buses and an insert, provided every
  routing node has pitch disabled. Groups with pitch enabled carry a resampler whose latency differs from
  other paths, and moving a source between them jumped its phase (~470 clicks).
- **Push stream**: refilled to 100 ms per video frame, 0 underruns and 0 clicks. Pushing exactly one frame's
  worth instead drifts (+53 ms per minute against PipeWire), so video audio keeps needing drift control.
- **Device switch**: 4–18 ms, playback resumes where it was; the graph isn't rebuilt.
- **Latency on PipeWire** (PulseAudio backend): 256-frame periods give a 16 ms buffer with no late callbacks
  (BASS uses a 20 ms device buffer); 64/128 work but arrive in bursts. ALSA can't open the device while
  PipeWire owns it, JACK needs pipewire-jack; miniaudio 0.11 has no native PipeWire backend.
- **Seeking a 10.5 min MP3**: under 0.1 ms with the seek table; load 8 ms; waveform decode ~0.45 s.
- **Windows**: `tixlaudio.dll` cross-compiles cleanly, exports all entry points and imports only the UCRT and
  KERNEL32. Not run yet.

## Open findings

- **MP3 seek-table seeks land one MP3 frame (1152 samples) late** relative to miniaudio's own linear decode;
  brute-force seeks are exact. dr_mp3's seek-table path doesn't account for its gapless (LAME delay) handling
  the way linear decoding does. Needs a local fix and an upstream report before soundtracks rely on it.
- **MP3 start offset vs. BASS**: decoding linearly, BASS's sample n is miniaudio's sample n+529 (12 ms), i.e.
  the two trim the decoder delay differently. Existing projects synced to BASS would hear MP3 soundtracks
  12 ms later unless compensated.

- **A seek kept stale audio** (fixed here): sounds that resample read input ahead into a cache that a seek
  didn't drop, so the old position played first. `native/miniaudio.h` carries a two-line patch, marked
  "TiXL patch". Push sources additionally run without pitch so they never read ahead.

## Not covered yet

- Running on Windows (WASAPI, loopback) and macOS (CoreAudio).
- Reverb and compressor nodes that sound like the DX8 ones.
- Capture analysis off the capture thread; per-source FFT taps; 3D audio routed through the mix.
