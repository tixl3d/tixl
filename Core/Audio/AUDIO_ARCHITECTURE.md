# Audio System Architecture

All audio runs in a small native library (`Native/TixlAudio/`, built on miniaudio). No managed code ever runs on an audio
or capture thread, so a garbage collection can't stall audio. C# calls flat, blittable functions from the main thread
once per frame: it sets parameters, rewires nodes and reads results.

## Signal flow

```
Soundtrack clip ─────────────────────────────► Soundtrack bus ─┐
                                                               ├─► Output ─► device, or an export's render
Sample, tone, video audio, spatial source ──► Operator bus ────┘
                     │                            ▲
                     └──► [AudioBus] ─► inserts ──┘
```

- Every source and bus has its own **tap**: a mono history that doesn't consume anything when read. That is what
  `[AudioLevel]`, `[AudioReaction]` and the meters read, as often as they like, and a stopped source reads as silence.
- The mix runs at a fixed 48 kHz (`AudioConfig.MixerFrequency`); the device connection converts if the hardware differs.
- A handle (`AudioHandle`) names a source or a bus. It carries a generation, so one kept past its release fails rather
  than reaching whatever reused the slot.
- Rewiring is queued and applied by the audio thread between blocks, so no block sees half a change. Routing nodes have
  pitch disabled, because a resampler's latency would make a source jump when it moves between paths.

## Classes

| Class | Role |
|---|---|
| `NativeAudio` | P/Invoke surface. Bind every entry point early: the first call to each allocates. |
| `NativeAudioEngine` | One engine: sources, buses, routes, levels, capture, device attach and detach. |
| `AudioMixerManager` | Owns the engine and the operator and soundtrack buses. |
| `AudioEngine` | Per-frame entry point: soundtrack clips, operator playback, stale detection, live FFT. |
| `SoundtrackClipStream` | A timeline clip. The timeline is master: it plays freely and is reseeked past a drift threshold. |
| `OperatorAudioStreamBase` | File sources driven by operators, with `StereoOperatorAudioStream` and `SpatialOperatorAudioStream`. |
| `ToneGeneratorSource` | Waveform and ADSR, computed natively. |
| `VideoAudioStream` | Push source fed by the video decoder, which owns its sync. |
| `AudioBusRouter`, `AudioGraphCollector` | Realise the audio graph as native buses; each insert becomes a nested bus. |
| `AudioInput` | Capture. The engine fills a ring buffer, and analysis, beat tracking and recording run here on the main thread. |
| `AudioRendering` | Export. |
| `OfflineAudioDecoder` | Decodes without playing, on any thread: waveform images and duration probes. |

## Analysis

`Fft` reproduces BASS's `FFT2048`, which existing projects are tuned to: Hann window, channels averaged, magnitudes
scaled so a full-scale sine reads about 1. `AudioAnalysisContext` turns that into bands, peaks and attacks. Audio input
runs it in 10 ms steps inside each frame, because the decay and onset detection are tuned to that rate.

## Export

`AudioRendering` detaches the device, so the same graph is pulled per video frame instead of being rebuilt, and exports
sound like live playback. Video audio is pushed synchronously per frame. Re-attaching resumes live playback. Without any
output device the engine runs in offline mode, so exports keep their audio.

## Spatial audio

Spatial sources are mono sources in the operator bus, using miniaudio's spatializer with a linear falloff. TiXL's world
is left-handed and miniaudio's right-handed, so `NativeAudioEngine` mirrors Z at the boundary. Cones use full angles. A
centred source sits at 0.5 per channel, and the far side keeps 20%.

## Formats

WAV, AIFF, MP3, OGG and FLAC. The vendored `miniaudio.h` carries patches, listed in `Native/TixlAudio/README.md`.

## Testing

`Core.Tests/NativeAudioEngineTests.cs` runs the engine offline. `.agentic/DEBUG_PROTOCOL.md` describes rendering and
audio input through the debug bridge; to hear nothing while testing, route the stream to a PipeWire null sink.
