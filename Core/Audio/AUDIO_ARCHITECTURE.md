# Audio System Architecture

**Version:** 2.5  
**Last Updated:** 2026-02-05
**Status:** Production Ready

---

## Table of Contents
1. [Introduction](#introduction)
2. [Architecture Overview](#architecture-overview)
3. [Core Components](#core-components)
4. [Class Hierarchy](#class-hierarchy)
5. [Mixer Architecture](#mixer-architecture)
6. [Signal Flow](#signal-flow)
7. [Playback Control Semantics](#playback-control-semantics)
8. [Stale Detection](#stale-detection)
9. [Audio Operators](#audio-operators)
10. [Configuration System](#configuration-system)
11. [Export and Rendering](#export-and-rendering)
12. [Audio Analysis and Buffer Ownership](#audio-analysis-and-buffer-ownership)
13. [Technical Implementation Details](#technical-implementation-details)
14. [Future Enhancement Opportunities](#future-enhancement-opportunities)
15. [Diff Summary](#diff-summary)

---

## Introduction

The TiXL audio system is a high-performance, low-latency audio engine built on ManagedBass, supporting stereo and 3D spatial audio playback within operator graphs.

### Key Features
- **Dual-mode playback**: Stereo (via mixer) and 3D spatial audio (direct to BASS) operators
- **Device-native sample rate**: Automatically matches output device sample rate via WASAPI query
- **Low-latency configuration**: Configurable update periods and buffer sizes
- **Native 3D audio**: BASS 3D engine with directional cones, Doppler effects, velocity-based positioning (hardware-accelerated via direct BASS output)
- **Configurable 3D factors**: Distance, rolloff, and Doppler factors via `AudioConfig`
- **Real-time analysis**: FFT spectrum, waveform, and level metering for both live and export
- **Centralized configuration**: Single source of truth via `AudioConfig`
- **Debug control**: Suppressible logging for cleaner development experience
- **Isolated offline analysis**: Waveform image generation without interfering with playback
- **Stale detection**: Automatic muting of inactive operator streams per-frame
- **Export support**: BASS-native export mixer with automatic resampling for video export (soundtrack + operator mixing)
- **Unified codebase**: Common base class (`OperatorAudioStreamBase`) for stereo streams; standalone class for spatial
- **FLAC support**: Native BASS FLAC plugin for high-quality audio files
- **External audio mode support**: Handles external device audio sources during export (operators only)
- **Batched 3D updates**: `Apply3D()` called once per frame for optimal performance
- **ADSR envelope support**: Built-in envelope generator for amplitude modulation on `AudioPlayer`

---

## Architecture Overview

```
                              AUDIO ENGINE (API)
    ┌───────────────────────────────────────────────────────────────┐
    │  UpdateStereoOperatorPlayback()    Set3DListenerPosition()    │
    │  UpdateSpatialOperatorPlayback()   CompleteFrame()            │
    │  UseSoundtrackClip()               ReloadSoundtrackClip()     │
    │  PauseOperator/ResumeOperator      GetOperatorLevel           │
    │  PauseSpatialOperator/Resume       GetSpatialOperatorLevel    │
    │  IsSpatialOperatorStreamPlaying()  IsSpatialOperatorPaused()  │
    │  UnregisterOperator()              SetGlobalVolume/Mute       │
    │  OnAudioDeviceChanged()            SetSoundtrackMute()        │
    │  TryGetStereoOperatorStream()      TryGetSpatialOperatorStream│
    │  GetClipChannelCount()             GetClipSampleRate()        │
    │  GetAllStereoOperatorStates()      GetAllSpatialOperatorStates│
    │  Get3DListenerPosition/Forward/Up                             │
    └───────────────────────────────────────────────────────────────┘
                       │                              │
                       ▼                              ▼
    ┌─────────────────────────────┐    ┌─────────────────────────────┐
    │   AUDIO MIXER MANAGER       │    │      AUDIO CONFIG           │
    │   (BASS Mixer)              │    │      (Configuration)        │
    ├─────────────────────────────┤    ├─────────────────────────────┤
    │  GlobalMixerHandle          │    │  MixerFrequency (from dev)  │
    │  OperatorMixerHandle        │    │  UpdatePeriodMs = 10        │
    │  SoundtrackMixerHandle      │    │  PlaybackBufferLengthMs=100 │
    │  CreateOfflineAnalysisStream│    │  DeviceBufferLengthMs = 20  │
    │  FreeOfflineAnalysisStream  │    │  FftBufferSize = 1024       │
    │  GetGlobalMixerLevel()      │    │  FrequencyBandCount = 32    │
    │  GetOperatorMixerLevel()    │    │  WaveformSampleCount = 1024 │
    │  GetSoundtrackMixerLevel()  │    │  DistanceFactor = 1.0       │
    │  SetGlobalVolume/Mute()     │    │  RolloffFactor = 1.0        │
    │  SetOperatorMute()          │    │  DopplerFactor = 1.0        │
    │                             │    │  LogAudioDebug/Info/Render  │
    │                             │    │  ShowAudioLogs toggle       │
    │                             │    │  ShowAudioRenderLogs toggle │
    └─────────────────────────────┘    └─────────────────────────────┘
                       │
                       ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │              OPERATOR AUDIO STREAM BASE (Abstract)              │
    ├─────────────────────────────────────────────────────────────────┤
    │  • Play/Pause/Resume/Stop      • Volume/Speed/Seek              │
    │  • Stale/User muting           • GetLevel (metering)            │
    │  • Export metering             • RenderAudio for export         │
    │  • PrepareForExport            • RestartAfterExport             │
    │  • UpdateFromBuffer            • ClearExportMetering            │
    │  • GetCurrentPosition          • Dispose                        │
    │  • SetStale(stale)             • TryLoadStreamCore (static)     │
    └─────────────────────────────────────────────────────────────────┘
                      │
          ┌───────────┴───────────┐
          ▼                       ▼
    ┌─────────────────┐    ┌─────────────────────────┐
    │  STEREO STREAM  │    │     SPATIAL STREAM      │
    ├─────────────────┤    ├─────────────────────────┤
    │  • SetPanning() │    │  • 3D Position          │
    │  • TryLoadStream│    │  • Orientation/Velocity │
    │  • Uses Mixer   │    │  • Cone/Doppler         │
    │                 │    │  • Apply3D()            │
    │                 │    │  • Set3DMode()          │
    │                 │    │  • Initialize3D         │
    │                 │    │  • DIRECT to BASS (no   │
    │                 │    │    mixer for HW 3D)     │
    └─────────────────┘    └─────────────────────────┘
                       │
                       ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │                    OPERATOR GRAPH INTEGRATION                   │
    ├────────────────────────────────┬────────────────────────────────┤
    │     AudioPlayer                │       SpatialAudioPlayer       │
    │     (Uses AudioPlayerUtils)    │       (Uses AudioPlayerUtils)  │
    └────────────────────────────────┴────────────────────────────────┘
```

### Implementation Overview

The audio engine in TiXL is built around ManagedBass / BassMix and a mixer-centric architecture managed by `AudioMixerManager`:

- **Mixing Architecture** (`AudioMixerManager.cs`)
  - Initializes BASS with low-latency configuration (UpdatePeriod, buffer lengths, LATENCY flag).
  - Creates:
    - `GlobalMixerHandle` (stereo, float, non-stop) → connected to sound device.
    - `OperatorMixerHandle` (decode, float, non-stop) → added to global mixer.
    - `SoundtrackMixerHandle` (decode, float, non-stop) → added to global mixer.
    - `OfflineMixerHandle` (decode, float) for analysis, not connected to output.
  - Loads `bassflac.dll` plugin.
  - Provides volume/mute and mixer level accessors.

- **Soundtrack / Timeline Audio** (`AudioEngine.cs`, `SoundtrackClipStream`, `SoundtrackClipDefinition.cs`)
  - `AudioEngine.SoundtrackClipStreams` maps `AudioClipResourceHandle` → `SoundtrackClipStream`.
  - Per-frame: `UseSoundtrackClip` marks clips used; `CompleteFrame` calls `ProcessSoundtrackClips`.
  - Soundtrack clips are played via `SoundtrackMixerHandle` for live playback.
  - For export, `AudioRendering` temporarily removes soundtrack streams from mixer and reads directly.
  - FFT and waveform analysis done via `UpdateFftBufferFromSoundtrack` into `AudioAnalysisContext` buffers.

- **Operator Audio (Clip Operators)** (`AudioEngine.cs`, `StereoOperatorAudioStream.cs`, `SpatialOperatorAudioStream.cs`, `OperatorAudioStreamBase.cs`)
  - Two dictionaries of operator state:
    - `_stereoOperatorStates: Guid → OperatorAudioState<StereoOperatorAudioStream>`
    - `_spatialOperatorStates: Guid → OperatorAudioState<SpatialOperatorAudioStream>`
  - Per frame, operators call `UpdateStereoOperatorPlayback` / `UpdateSpatialOperatorPlayback` with parameters:
    - file path, play/stop triggers, volume/mute, panning or 3D position, speed, normalized seek.
  - `OperatorAudioState<T>` tracks stream instance, current file path, play/stop edges, seek, pause, stale flag.
  - Streams feed into `OperatorMixerHandle` which mixes into the global mixer.

- **Stale / Lifetime Management** (`AudioEngine.cs`, `STALE_DETECTION.md`)
  - Uses internal monotonic frame token (`_audioFrameToken`) for stale detection.
  - Each operator state tracks `LastUpdatedFrameId` to determine if it was updated this frame.
  - `StopStaleOperators()` runs in `CompleteFrame()` before operators are evaluated, marking operators that weren't updated in the previous frame as stale.
  - `EnsureFrameTokenCurrent()` is called in `CompleteFrame()` **after** stale checking to ensure the token increments even when no audio operators are updated.
  - This guarantees stale detection works correctly when navigating away from audio operators.
  - During export, special export/reset functions mark streams stale and restore after export.

- **Audio Rendering / Export Path** (`AudioRendering.cs`)
  - `PrepareRecording`: pauses global mixer, saves state, clears export registry, resets operator streams.
  - Creates dedicated export mixer for sample-accurate seeking and BASS-handled resampling.
  - Removes soundtrack streams from `SoundtrackMixerHandle`, adds them to export mixer.
  - `GetFullMixDownBuffer` reads from export mixer (BASS handles resampling), mixes operator audio.
  - Uses reusable static buffers to minimize per-frame allocations.
  - Logs per-frame stats, updates meter levels for operator streams.
  - `EndRecording`: re-adds soundtrack streams to mixer, restores saved state, resumes playback.

- **Analysis / Metering / Input**
  - `AudioAnalysis`, `WaveFormProcessing`, `AudioImageGenerator`, `WasapiAudioInput` provide FFT, waveform, input capture, and offline analysis.
  - Export metering uses `AudioExportSourceRegistry` and `AudioRendering.EvaluateAllAudioMeteringOutputs` to evaluate operator graph outputs on offline buffers.

---

## Core Components

### AudioEngine
The central API for all audio operations. Key responsibilities:
- **Soundtrack Management**: `UseSoundtrackClip()`, `ReloadSoundtrackClip()`, `CompleteFrame()`
- **Operator Playback**: `UpdateStereoOperatorPlayback()`, `UpdateSpatialOperatorPlayback()`
- **3D Listener**: `Set3DListenerPosition()`, `Get3DListenerPosition/Forward/Up()`
- **State Queries**: `IsOperatorStreamPlaying()`, `IsOperatorPaused()`, `GetOperatorLevel()`
- **Spatial State Queries**: `IsSpatialOperatorStreamPlaying()`, `IsSpatialOperatorPaused()`, `GetSpatialOperatorLevel()`
- **Device Management**: `OnAudioDeviceChanged()`, `SetGlobalVolume()`, `SetGlobalMute()`
- **Export Support**: `ResetAllOperatorStreamsForExport()`, `RestoreOperatorAudioStreams()`, `UpdateStaleStatesForExport()`
- **Export Metering**: `GetAllStereoOperatorStates()`, `GetAllSpatialOperatorStates()`

### AudioMixerManager
Manages the BASS mixer hierarchy. Key features:
- **Automatic device sample rate detection** via WASAPI query before BASS init
- **Low-latency configuration** applied before initialization
- **FLAC plugin loading** for native FLAC support
- **Four mixer handles** for different purposes (see Mixer Architecture)
- **Level metering** via `GetGlobalMixerLevel()`, `GetOperatorMixerLevel()`, `GetSoundtrackMixerLevel()`
- **Offline analysis streams** via `CreateOfflineAnalysisStream()` / `FreeOfflineAnalysisStream()`

### AudioConfig
Centralized configuration with compile-time and runtime settings:
- **Runtime**: `MixerFrequency` (set from device), `ShowAudioLogs`, `ShowAudioRenderLogs`
- **3D Audio**: `DistanceFactor`, `RolloffFactor`, `DopplerFactor` (configurable at runtime)
- **Compile-time**: Buffer sizes, FFT configuration, frequency band counts
- **Logging helpers**: `LogAudioDebug()`, `LogAudioInfo()`, `LogAudioRenderDebug()`, `LogAudioRenderInfo()`

---

## Class Hierarchy

```
OperatorAudioStreamBase (abstract)
├── Properties: Duration, StreamHandle, MixerStreamHandle, IsPaused, IsPlaying, FilePath
├── Protected: DefaultPlaybackFrequency, CachedChannels, CachedFrequency, IsStaleStopped
├── Methods: Play, Pause, Resume, Stop, SetVolume, SetSpeed, Seek
├── Metering: GetLevel, UpdateFromBuffer, ClearExportMetering
├── Export: PrepareForExport, RestartAfterExport, RenderAudio, GetCurrentPosition
│
└── StereoOperatorAudioStream (extends base, uses mixer)
    ├── TryLoadStream(filePath, mixerHandle) - Factory method
    └── SetPanning(float) - Pan audio left (-1) to right (+1)

SpatialOperatorAudioStream (standalone class - does NOT inherit from base)
├── Properties: Duration, StreamHandle, FilePath, IsPaused, IsPlaying, IsStaleStopped
├── Methods: Play, Pause, Resume, Stop, SetVolume, SetSpeed, Seek
├── 3D Methods:
│   ├── Initialize3DAudio() - Setup initial 3D attributes
│   ├── Update3DPosition(Vector3, float, float) - Position + min/max distance
│   ├── Set3DOrientation(Vector3) - Directional facing
│   ├── Set3DCone(float, float, float) - Inner/outer angle + volume
│   └── Set3DMode(Mode3D) - Normal/Relative/Off
├── TryLoadStream(filePath, mixerHandle) - Factory (mixerHandle ignored, plays direct)
├── Metering: GetLevel, UpdateFromBuffer (export)
├── Export State: _exportDecodeStreamHandle, _isExportMode, _exportPlaybackPosition
├── Distance Attenuation: _distanceAttenuation, _isBeyondMaxDistance (linear rolloff)
└── Note: Plays DIRECTLY to BASS output for hardware 3D processing

AudioPlayerUtils (static utility)
└── ComputeInstanceGuid(IEnumerable<Guid>) - Stable operator identification via FNV-1a hash

OperatorAudioUtils (static utility)
├── FillAndResample(...) - Buffer filling with resampling/channel conversion
└── LinearResample(...) - Simple linear resampler and up/down-mixer

AudioEngine (static)
├── Soundtrack: SoundtrackClipStreams, UseSoundtrackClip, ReloadSoundtrackClip
├── Operators: _stereoOperatorStates, _spatialOperatorStates, Update*OperatorPlayback
├── Internal State Classes:
│   ├── OperatorAudioState<T> - Stream, CurrentFilePath, IsPaused, PendingSeek, PreviousPlay/Stop, IsStale, LastUpdatedFrameId
│   └── SpatialOperatorState - Same structure but non-generic for spatial streams
├── 3D Listener: _listenerPosition, _listenerForward, _listenerUp, Set3DListenerPosition
├── 3D Batching: Mark3DApplyNeeded(), Apply3DChanges() (called once per frame)
├── Stale Detection: _audioFrameToken (monotonic), LastUpdatedFrameId per operator, StopStaleOperators
├── Export: ResetAllOperatorStreamsForExport, RestoreOperatorAudioStreams, UpdateStaleStatesForExport
└── Device: OnAudioDeviceChanged, DisposeAllAudioStreams, SetGlobalVolume, SetGlobalMute
```

---

## Mixer Architecture

The mixer architecture uses a hierarchical structure with separate paths for different audio sources:

### Mixer Handles

| Handle | Flags | Purpose |
|--------|-------|---------|
| **GlobalMixerHandle** | `Float \| MixerNonStop` | Master output to soundcard |
| **OperatorMixerHandle** | `MixerNonStop \| Decode \| Float` | Operator audio decode submixer |
| **SoundtrackMixerHandle** | `MixerNonStop \| Decode \| Float` | Soundtrack decode submixer |

> **Offline Analysis:** Waveform and FFT analysis uses standalone decode streams created via 
> `CreateOfflineAnalysisStream()`. These streams are independent and do not use a mixer.

### Live Playback Path
```
Stereo Operator Clips ──► OperatorMixer (Decode) ──────┐
                          [MixerChanBuffer]            │
                                                       ├──► GlobalMixer ──► Soundcard
Soundtrack Clips ──► SoundtrackMixer (Decode) ─────────┘
                     [MixerChanBuffer]

Spatial Operator Clips ──► BASS Direct (3D Flags) ──────► Soundcard (hardware 3D)
                           [Mono + Bass3D + Float]
```

> **Note:** Spatial streams bypass the mixer hierarchy entirely to enable hardware-accelerated 3D audio 
> processing with native BASS 3D engine support. They play directly to the BASS output device.

### Export Path (GlobalMixer Paused)
```
Soundtrack Clips ──► Export Mixer ────────────────┐
                     (BassMix resampling)         │
                     (removed from SoundtrackMixer│
                                                  ├──► MixBuffer ──► Video Encoder
OperatorMixer ──► ChannelGetData() ───────────────┤
                  (stereo decode)                 │
                                                  │
Spatial Streams ──► RenderAudio() ────────────────┘
                    (decode stream + manual 3D)
```

### Isolated Analysis (No Output)
```
AudioFile ──► CreateOfflineAnalysisStream() ──► FFT/Waveform ──► Image Generation
              (Decode + Prescan flags)          (no soundcard)
```

### Stereo vs Spatial Output Flow

| Aspect | Stereo Streams | Spatial Streams |
|--------|----------------|-----------------|
| **Output Path** | Through OperatorMixer → GlobalMixer → Soundcard | Direct to BASS → Soundcard |
| **Stream Flags** | `Decode \| Float \| AsyncFile` | `Float \| Mono \| Bass3D \| AsyncFile` |
| **Mixer Channel** | Added via `BassMix.MixerAddChannel` | Not added to any mixer |
| **Playback Method** | `BassMix.ChannelPlay` | `Bass.ChannelPlay` |
| **3D Processing** | None (2D stereo) | Hardware-accelerated via BASS 3D engine |
| **Level Metering** | `BassMix.ChannelGetLevel` | `Bass.ChannelGetLevel` |
| **Why?** | Mixer provides flexible routing, volume control | 3D requires native BASS for HW acceleration |

> **Design Decision:** Spatial audio bypasses the mixer to leverage BASS's hardware-accelerated 3D 
> positioning. Routing through the mixer would break the native 3D audio chain, as the mixer outputs 
> standard stereo which cannot be repositioned in 3D space afterwards.

---

## Signal Flow

### Stereo Audio (Uses Mixer)
```
AudioFile ──► Bass.CreateStream (Decode|Float|AsyncFile)
          │
          ├──► BassMix.MixerAddChannel (MixerChanBuffer|MixerChanPause)
          │                │
          │                └──► OperatorMixer ──► GlobalMixer ──► Soundcard
          │
          └──► SetVolume/SetPanning/SetSpeed ──► BassMix.ChannelGetLevel ──► Metering
```

### Spatial Audio (Direct to BASS - Hardware 3D)
```
AudioFile ──► Bass.CreateStream (Float|Mono|Bass3D|AsyncFile)
          │
          │   [NO MIXER - Direct to BASS Output]
          │
          ├──► Bass.ChannelPlay() ──────────────────────────────► Soundcard (HW 3D)
          │
          ├──► 3D Position ──► Bass.ChannelSet3DPosition() ──────┐
          │                                                       │
          ├──► 3D Attributes ──► Bass.ChannelSet3DAttributes() ──┼──► Bass.Apply3D()
          │    (Mode, Distance, Cone)                             │
          │                                                       │
          └──► Velocity ──► Doppler Effect ──────────────────────┘
```

### FFT and Waveform Analysis (Live)
```
GlobalMixer ──► Bass.ChannelGetData(FFT2048) ──► FftGainBuffer ──► ProcessUpdate()
            │                                                          │
            │                                    ┌─────────────────────┘
            │                                    ▼
            │                              FrequencyBands[32]
            │                              FrequencyBandPeaks[32]
            │                              FrequencyBandAttacks[32]
            │
            └──► Bass.ChannelGetData(samples) ──► InterleavenSampleBuffer
                                                          │
                                                          ▼
                                                    WaveformLeftBuffer[1024]
                                                    WaveformRightBuffer[1024]
                                                    WaveformLow/Mid/HighBuffer[1024]
```

---

## Playback Control Semantics

### Trigger-Based Controls
Play, Stop, and Pause use **rising edge detection**:
- `shouldPlay`: Starts playback when transitioning from `false` to `true`
- `shouldStop`: Stops playback and resets position when transitioning from `false` to `true`
- `shouldPause`: Pauses/resumes based on current value (not edge-triggered)

### Seek Semantics (Pending Seek Model)

The `seek` parameter (0.0 to 1.0 normalized position) uses a **pending seek model**:

```
┌─────────────────────────────────────────────────────────────────┐
│  Frame 1: seek = 0.5                                            │
│           → PendingSeek stored as 0.5                           │
│           → No immediate effect on playback                     │
│                                                                 │
│  Frame 2: seek = 0.5, shouldPlay = true (rising edge)           │
│           → PendingSeek (0.5) applied via Stream.Seek()         │
│           → Stream.Play() called                                │
│           → Playback starts at 50% position                     │
│                                                                 │
│  Frame 3: seek = 0.7 (while playing)                            │
│           → PendingSeek updated to 0.7                          │
│           → Playback continues from current position            │
│           → Seek will apply on next play trigger                │
│                                                                 │
│  Frame 4: shouldStop = true (rising edge)                       │
│           → Playback stops                                      │
│           → PendingSeek reset to 0                              │
└─────────────────────────────────────────────────────────────────┘
```

**Key Behaviors:**
- Seek value is **stored** as `PendingSeek`, not immediately applied
- Seek is **applied before playback** when play is triggered
- Changing seek **during playback has no effect** until next play trigger
- Stop trigger **resets** `PendingSeek` to 0
- This allows setting seek + play in the **same frame** for predictable behavior

**Why This Design:**
- Avoids repeated BASS seek calls during playback (performance)
- Eliminates ambiguity about 0 meaning "no seek" vs "seek to start"
- Makes operator behavior predictable when upstream controls change rapidly
- Matches user expectation: "set position, then press play"

---

## Stale Detection

The audio engine automatically stops operator audio streams that are no longer being updated. This prevents "orphaned" audio from operators that have been disabled, deleted, or removed from the evaluation graph.

### How It Works

**Operator Contract:** Every audio operator must call its update method (`UpdateStereoOperatorPlayback` or `UpdateSpatialOperatorPlayback`) every frame to maintain active playback.

**Frame Token System:** The engine uses an internal monotonic frame token (`_audioFrameToken`) to track which operators were updated each frame. Each operator state has a `LastUpdatedFrameId` that is set when updated.

**Detection Flow:**
1. `CompleteFrame()` is called at the start of each frame
2. `StopStaleOperators()` marks operators where `LastUpdatedFrameId != _audioFrameToken` as stale
3. Stale streams are paused via `SetStale(true)`
4. When a stale operator is updated again, it's marked active and playback can resume

**Export Behavior:** Stale detection is bypassed during export (`IsRenderingToFile`). Operator states are explicitly managed via `ResetAllOperatorStreamsForExport()` and `RestoreOperatorAudioStreams()`.

### Troubleshooting

| Symptom | Cause | Solution |
|---------|-------|----------|
| Audio cuts out unexpectedly | Operator missed an update frame | Ensure update is called every frame unconditionally |
| Audio doesn't restart after disable/enable | Stale detection stopped the audio | Send `shouldPlay = true` trigger when re-enabling |
| Audio plays briefly then stops | Operator only updated conditionally | Update every frame, even when parameters don't change |

---

## Audio Operators

### AudioPlayer
**Purpose:** High-quality stereo audio playback with real-time control, ADSR envelope, and analysis.

**Key Parameters:**
- Playback control: Play, Stop, Pause (trigger-based, rising edge detection)
- Audio parameters: Volume (0-1), Mute, Panning (-1 to 1), Speed (0.1x to 4x)
- **Seek (0-1 normalized)**: Stored as pending position, applied only on play trigger
- **ADSR Envelope**: TriggerMode (OneShot/Gate/Loop), Duration, UseEnvelope toggle
- **Envelope Vector4**: X=Attack, Y=Decay, Z=Sustain (level), W=Release
- Analysis outputs: IsPlaying, GetLevel (0-1)

**Seek Behavior:**
The seek parameter uses "pending seek" semantics:
- Changing seek during playback has **no immediate effect**
- Seek value is stored and applied when play is triggered
- This allows setting seek + play in the same frame for predictable behavior
- Stop trigger resets pending seek to 0

**Implementation Details:**
- Uses `AudioPlayerUtils.ComputeInstanceGuid()` for stable operator identification
- Delegates all audio logic to `AudioEngine.UpdateStereoOperatorPlayback()`
- Supports `RenderAudio()` for export functionality
- **ADSR via `AdsrCalculator`**: Envelope modulates volume in real-time when `UseEnvelope` is enabled
- Finalizer unregisters operator from AudioEngine via `UnregisterOperator()`

**Use Cases:**
- Background music playback
- Sound effect triggering with ADSR envelope shaping
- Audio-reactive visuals
- Beat detection integration

### SpatialAudioPlayer
**Purpose:** 3D spatial audio with native BASS 3D engine for immersive soundscapes.

**Key Parameters:**
- Playback: Play, Stop, Pause (trigger-based, rising edge detection)
- Audio: Volume (0-1), Mute, Speed (0.1x to 4x)
- **Seek (0-1 normalized)**: Stored as pending position, applied only on play trigger
- **3D Source**: SourcePosition (Vector3), SourceRotation (Vector3 Euler degrees)
- **3D Listener**: ListenerPosition (Vector3), ListenerRotation (Vector3 Euler degrees)
- **Distance**: MinDistance, MaxDistance for attenuation
- **Directionality**: InnerConeAngle (0-360°), OuterConeAngle (0-360°), OuterConeVolume (0-1)
- **Advanced**: Audio3DMode (Normal/Relative/Off), GizmoVisibility

**Outputs:**
- `Result`: Command output for operator chaining
- `GizmoOutput`: Internal gizmo rendering output
- `IsPlaying`, `IsPaused`: Playback state queries
- `GetLevel`: Current audio amplitude (0-1)

**Implementation Details:**
- **Implements `ITransformable`** for gizmo manipulation (TranslationInput → SourcePosition, RotationInput → SourceRotation)
- **Loads audio as mono** with `BassFlags.Bass3D | BassFlags.Mono | BassFlags.Float` for optimal 3D positioning
- **Plays directly to BASS output** (NOT through OperatorMixer) for hardware-accelerated 3D processing
- Does not use `BassMix.MixerAddChannel` - the `mixerHandle` parameter is ignored in `TryLoadStream`
- Listener rotation converted from Euler angles to forward/up vectors via rotation matrix
- 3D position updated every frame via `AudioEngine.Set3DListenerPosition()`
- Velocity computed from position delta (assumes ~60fps) for Doppler effects
- **Linear distance attenuation**: Custom rolloff from minDistance to maxDistance (BASS uses inverse distance)
- Uses `AudioEngine.Mark3DApplyNeeded()` to batch 3D changes per frame
- `Bass.Apply3D()` called once per frame in `CompleteFrame()` for performance
- Supports `RenderAudio()` for export functionality (uses separate decode stream)

**Use Cases:**
- 3D environments and games
- Spatial audio installations
- Directional speakers/emitters
- Doppler effect simulations

---

## Configuration System

### AudioConfig (Centralized Configuration)

All audio parameters are managed through `Core/Audio/AudioConfig.cs`:

**Mixer Configuration:**
```csharp
MixerFrequency       // Determined from device's current sample rate (runtime)
UpdatePeriodMs = 10              // Low-latency BASS updates
UpdateThreads = 2                // BASS update thread count
PlaybackBufferLengthMs = 100     // Balanced buffering
DeviceBufferLengthMs = 20        // Minimal device latency
```

**3D Audio Configuration:**
```csharp
DistanceFactor = 1.0f            // Units per meter (1.0 = 1 unit = 1 meter)
RolloffFactor = 1.0f             // Distance attenuation (0 = none, 1 = real-world)
DopplerFactor = 1.0f             // Doppler effect strength (0 = none, 1 = real-world)
```

**FFT and Analysis:**
```csharp
FftBufferSize = 1024             // FFT resolution
BassFftDataFlag = DataFlags.FFT2048  // Returns 1024 values
FrequencyBandCount = 32          // Spectrum bands
WaveformSampleCount = 1024       // Waveform resolution
LowPassCutoffFrequency = 250f    // Low frequency separation (Hz)
HighPassCutoffFrequency = 2000f  // High frequency separation (Hz)
```

**Logging Control:**
```csharp
ShowAudioLogs = false            // Toggle audio debug/info logs
ShowAudioRenderLogs = false      // Toggle audio rendering logs
LogAudioDebug(message)           // Suppressible debug logging
LogAudioInfo(message)            // Suppressible info logging
LogAudioRenderDebug(message)     // Suppressible render debug logging
LogAudioRenderInfo(message)      // Suppressible render info logging
Initialize(showAudioLogs, showAudioRenderLogs)  // Editor initialization
```

**Centralized Gated Logging (Log.Gated):**

The audio system uses `Log.Gated` from `T3.Core.Logging` for category-based debug output. This provides a centralized, toggleable logging mechanism:

```csharp
// API Usage
Log.Gated.Audio("message")           // Audio system debug messages
Log.Gated.AudioRender("message")     // Audio rendering/export debug messages
Log.Gated.VideoRender("message")     // Video rendering debug messages

// Enable/Disable at runtime
Log.Gated.AudioEnabled = true        // Toggle audio logging
Log.Gated.AudioRenderEnabled = true  // Toggle audio render logging
Log.Gated.VideoRenderEnabled = true  // Toggle video render logging

// Initialize all categories at startup
Log.Gated.Initialize(audio, audioRender, videoRender)
```

Messages are only logged when their respective category is enabled, reducing log noise during normal operation while allowing detailed debugging when needed.

### User Settings Integration

Audio configuration is accessible through the Editor Settings window:

**Location:** `Settings → Profiling and Debugging → Audio System`

**Available Settings:**
- ✅ Show Audio Debug Logs (real-time toggle)
- ✅ Show Audio Render Logs (for export debugging)

**Persistence:** All settings are saved to `UserSettings.json` and restored on startup.

---

## Export and Rendering

### Export Flow

```
PrepareRecording()
        │
        ├──► Pause GlobalMixer
        ├──► Clear AudioExportSourceRegistry
        ├──► Reset WaveFormProcessing export buffer
        ├──► ResetAllOperatorStreamsForExport() (both stereo + spatial)
        ├──► Create Export Mixer (BassMix, Decode + Float + MixerNonStop)
        └──► Remove Soundtrack streams from SoundtrackMixer, add to Export Mixer
        │
        ▼
GetFullMixDownBuffer() [per frame]
        │
        ├──► UpdateStaleStatesForExport() (both stereo + spatial)
        ├──► MixSoundtracksFromExportMixer() ──► Position + Volume + ChannelGetData
        ├──► MixOperatorAudio() ──► Read from OperatorMixer (stereo only)
        ├──► MixSpatialOperatorAudio() ──► Read from decode streams (spatial)
        ├──► UpdateOperatorMetering() (both stereo + spatial)
        ├──► PopulateFromExportBuffer() ──► WaveForm buffers
        └──► ComputeFftFromBuffer() ──► FFT buffers
        │
        ▼
EndRecording()
        │
        ├──► Remove Soundtrack streams from Export Mixer
        ├──► Re-add Soundtrack streams to SoundtrackMixer
        ├──► Free Export Mixer
        ├──► RestoreState() (export state)
        └──► RestoreOperatorAudioStreams()
```

### Export Mixer Design
The export system uses a dedicated BASS mixer (`_exportMixerHandle`) that handles resampling automatically:
- Created with `BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop`
- Soundtrack streams are added with `BassFlags.MixerChanNoRampin | BassFlags.MixerChanPause`
- BASS handles resampling from each clip's native frequency to the mixer frequency

### External Audio Mode
When `AudioSource` is set to `ExternalDevice` during export:
- Soundtrack mixing is skipped entirely
- Only operator audio is included in export
- Warning is logged to inform user
- Waveform buffers are cleared (external audio can't be monitored)

### Spatial Audio Export Notes
During export, spatial audio streams require special handling:
- Spatial streams use a **separate decode stream** (`_exportDecodeStreamHandle`) for reading audio data
- The hardware 3D processing is **not applied** during export (raw mono audio is exported)
- 3D positioning effects are only present during live playback
- Exported spatial audio is mixed as mono, then converted to stereo for the final mixdown

> **Important:** Spatial audio in exported videos will NOT include 3D positioning effects. (Doppler)
> The exported audio is the raw source audio mixed to stereo with simulated spatial processing.

---

## Audio Analysis and Buffer Ownership

### AudioAnalysisContext

All FFT and waveform analysis buffers are owned by `AudioAnalysisContext` instances. This design enables:

- **Clear ownership**: Each context owns its own set of buffers
- **Thread safety preparation**: Separate contexts can be used on different threads
- **Testability**: Contexts can be created and manipulated independently

### Default Context

For backwards compatibility, a singleton `AudioAnalysisContext.Default` is provided:

```csharp
// Static accessors delegate to the default context
AudioAnalysis.FftGainBuffer        // → AudioAnalysisContext.Default.FftGainBuffer
AudioAnalysis.FrequencyBands       // → AudioAnalysisContext.Default.FrequencyBands
WaveFormProcessing.WaveformLeftBuffer  // → AudioAnalysisContext.Default.WaveformLeftBuffer
```

### Thread Safety

**Current Behavior:** The default context is designed for single-threaded use on the main update loop. All BASS channel reads and buffer processing occur on the main thread.

**Warning:** Do not access `AudioAnalysisContext.Default` from multiple threads without external synchronization.

### Multi-Threading Migration Path

To enable multi-threaded audio analysis:

1. **Create separate contexts** per thread/consumer:
   ```csharp
   var backgroundContext = new AudioAnalysisContext();
   ```

2. **Pass context explicitly** to analysis methods:
   ```csharp
   AudioEngine.UpdateFftBufferFromSoundtrack(playback, backgroundContext);
   WaveFormProcessing.UpdateWaveformData(backgroundContext);
   AudioAnalysis.ComputeFftFromBuffer(pcmBuffer, backgroundContext);
   ```

3. **Synchronize BASS channel reads** (BASS itself may have thread constraints):
   ```csharp
   lock (bassLock)
   {
       AudioEngine.UpdateFftBufferFromSoundtrack(playback, backgroundContext);
   }
   ```

4. **Use locks or concurrent collections** if sharing results between threads

### Example: Background Thread Analysis

```csharp
// Create a dedicated context for background analysis
var backgroundContext = new AudioAnalysisContext();
object bassLock = new object();

// On background thread:
void AnalyzeOnBackgroundThread(Playback playback)
{
    // Synchronize BASS access
    lock (bassLock)
    {
        AudioEngine.UpdateFftBufferFromSoundtrack(playback, backgroundContext);
    }
    
    // Process FFT data (no lock needed - context is thread-local)
    backgroundContext.ProcessFftUpdate();
    
    // Access results
    float[] bands = backgroundContext.FrequencyBands;
    float[] fft = backgroundContext.FftNormalizedBuffer;
    
    // Do something with the analysis results...
}
```

### Context Buffers

Each `AudioAnalysisContext` contains:

| Buffer | Size | Description |
|--------|------|-------------|
| `FftGainBuffer` | 1024 | Raw FFT gain values from BASS |
| `FftNormalizedBuffer` | 1024 | FFT values normalized to 0-1 |
| `FrequencyBands` | 32 | Frequency band levels |
| `FrequencyBandPeaks` | 32 | Peak-hold values with decay |
| `FrequencyBandAttacks` | 32 | Attack (rate of increase) values |
| `FrequencyBandAttackPeaks` | 32 | Peak attack values |
| `FrequencyBandOnSets` | 32 | Onset detection for beat sync |
| `InterleavedSampleBuffer` | 2048 | Raw interleaved stereo samples |
| `WaveformLeftBuffer` | 1024 | Left channel waveform |
| `WaveformRightBuffer` | 1024 | Right channel waveform |
| `WaveformLowBuffer` | 1024 | Low frequency filtered waveform |
| `WaveformMidBuffer` | 1024 | Mid frequency filtered waveform |
| `WaveformHighBuffer` | 1024 | High frequency filtered waveform |

---

## Technical Implementation Details

This section provides implementation-level details about the audio engine architecture built around ManagedBass / BassMix.

### Mixing Architecture (`AudioMixerManager.cs`)

The mixer manager initializes BASS with low-latency configuration (UpdatePeriod, buffer lengths, LATENCY flag) and creates:

- **`GlobalMixerHandle`** (stereo, float, non-stop) → connected to sound device
- **`OperatorMixerHandle`** (decode, float, non-stop) → added to global mixer
- **`SoundtrackMixerHandle`** (decode, float, non-stop) → added to global mixer
- **`OfflineMixerHandle`** (decode, float) for analysis, not connected to output

Additionally loads `bassflac.dll` plugin and provides volume/mute and mixer level accessors.

### Soundtrack / Timeline Audio

**Files:** `AudioEngine.cs`, `SoundtrackClipStream.cs`, `SoundtrackClipDefinition.cs`

- `AudioEngine.SoundtrackClipStreams` maps `AudioClipResourceHandle` → `SoundtrackClipStream`
- Per-frame: `UseSoundtrackClip` marks clips used; `CompleteFrame` calls `ProcessSoundtrackClips`
- Soundtrack clips are played via `SoundtrackMixerHandle` for live playback
- For export, `AudioRendering` temporarily removes soundtrack streams from mixer and reads directly
- FFT and waveform analysis done via `UpdateFftBufferFromSoundtrack` into `AudioAnalysisContext` buffers

### Operator Audio (Clip Operators)

**Files:** `AudioEngine.cs`, `StereoOperatorAudioStream.cs`, `SpatialOperatorAudioStream.cs`, `OperatorAudioStreamBase.cs`

Two dictionaries of operator state:
- `_stereoOperatorStates: Guid → OperatorAudioState<StereoOperatorAudioStream>`
- `_spatialOperatorStates: Guid → OperatorAudioState<SpatialOperatorAudioStream>`

Per frame, operators call `UpdateStereoOperatorPlayback` / `UpdateSpatialOperatorPlayback` with parameters:
- file path, play/stop triggers, volume/mute, panning or 3D position, speed, normalized seek

`OperatorAudioState<T>` tracks stream instance, current file path, play/stop edges, seek, pause, stale flag. Streams feed into `OperatorMixerHandle` which mixes into the global mixer.

### Stale / Lifetime Management

**Files:** `AudioEngine.cs`

- Uses internal monotonic frame token (`_audioFrameToken`) for stale detection
- Each operator state tracks `LastUpdatedFrameId` to determine if it was updated this frame
- `StopStaleOperators()` runs in `CompleteFrame()` before operators are evaluated, marking operators that weren't updated in the previous frame as stale
- `EnsureFrameTokenCurrent()` is called in `CompleteFrame()` **after** stale checking to ensure the token increments even when no audio operators are updated
- This guarantees stale detection works correctly when navigating away from audio operators
- During export, special export/reset functions mark streams stale and restore after export

### Audio Rendering / Export Path

**File:** `AudioRendering.cs`

- `PrepareRecording`: pauses global mixer, saves state, clears export registry, resets operator streams
- Creates dedicated export mixer for sample-accurate seeking and BASS-handled resampling
- Removes soundtrack streams from `SoundtrackMixerHandle`, adds them to export mixer
- `GetFullMixDownBuffer` reads from export mixer (BASS handles resampling), mixes operator audio
- Uses reusable static buffers to minimize per-frame allocations
- Logs per-frame stats, updates meter levels for operator streams
- `EndRecording`: re-adds soundtrack streams to mixer, restores saved state, resumes playback

### Analysis / Metering / Input

`AudioAnalysis`, `WaveFormProcessing`, `AudioImageGenerator`, `WasapiAudioInput` provide FFT, waveform, input capture, and offline analysis. Export metering uses `AudioExportSourceRegistry` and `AudioRendering.EvaluateAllAudioMeteringOutputs` to evaluate operator graph outputs on offline buffers.

### Known Technical Notes

1. **Level Metering**: `GetGlobalMixerLevel()` uses `Bass.ChannelGetLevel` with a 50ms window for metering. Ensure BASS version supports this overload.

2. **Offline Analysis Streams**: `CreateOfflineAnalysisStream` creates standalone decode streams, not added to the offline mixer. The `_offlineMixerHandle` exists for potential future multi-stream analysis but is currently unused.

3. **Seek Semantics**: Uses "pending seek" model where seek value is stored and applied only on play trigger. This avoids ambiguity between "no seek" and "seek to start" when using 0 as the value.

4. **Device Changes**: `AudioMixerManager.Shutdown()` frees all streams and calls `Bass.Free()`. Higher-level components should handle device change events appropriately to avoid invalid handle access.

---


## Future Enhancement Opportunities

### Environmental Audio (Not Started)
- EAX effects integration (reverb, echo, chorus) - BASS supports, not yet exposed
- Room acoustics simulation
- Environmental audio zones

### Advanced 3D Audio (Partial)
- ✓ **Doppler effects** - Implemented via velocity tracking
- ✓ **Directional cones** - Inner/outer angle with volume falloff
- ✓ **Distance attenuation** - Linear rolloff from min to max distance
- Custom distance rolloff curves (not implemented)
- Per-stream Doppler factor adjustment (not implemented)
- HRTF for headphone spatialization (not implemented)
- Geometry-based occlusion (not implemented)

### Current Limitations
1. No EAX environmental effects (BASS supports, not yet exposed)
2. Spatial audio not included in mixer-level metering (plays directly to BASS)
3. Export of spatial audio uses separate decode stream (no hardware 3D in export)
4. No custom distance rolloff curves
5. No per-stream Doppler factor adjustment

---

# Diff Summary

Diff summary for branch `Bass-AudioImplementation` vs `origin/main`

## Added

### Core Audio Files
- `Core/Audio/AUDIO_ARCHITECTURE.md` — new architecture/design doc for the audio subsystem.
- `Core/Audio/AdsrCalculator.cs` — ADSR envelope calculation utility.
- `Core/Audio/AudioAnalysisContext.cs` — owns all FFT/waveform buffers, enables multi-threaded analysis.
- `Core/Audio/AudioConfig.cs` — centralized audio configuration and logging toggles.
- `Core/Audio/AudioExportSourceRegistry.cs` — registry for export/record audio sources.
- `Core/Audio/AudioMixerManager.cs` — BASS mixer initialization/management and helpers.
- `Core/Audio/IAudioExportSource.cs` — interface for exportable audio sources.
- `Core/Audio/ISpatialAudioPropertiesProvider.cs` — interface for spatial audio properties.
- `Core/Audio/OperatorAudioStreamBase.cs` — abstract base for operator audio streams.
- `Core/Audio/OperatorAudioUtils.cs` — helper utilities for operator streams.
- `Core/Audio/SpatialOperatorAudioStream.cs` — spatial/3D operator stream implementation.
- `Core/Audio/StereoOperatorAudioStream.cs` — stereo operator stream implementation.

### Editor Files
- `Editor/Gui/InputUi/CombinedInputs/AdsrEnvelopeInputUi.cs` — UI input for ADSR envelope.
- `Editor/Gui/OpUis/UIs/AdsrEnvelopeUi.cs` — ADSR editor UI control.
- `Editor/Gui/UiHelpers/AudioLevelMeter.cs` — audio level meter UI component.
- `Editor/Gui/Windows/SettingsWindow.AudioPanel.cs` — audio panel for settings window.

### Operator Files
- `Operators/Lib/io/audio/AudioPlayer.cs` (+ `.t3`/`.t3ui`) — stereo audio operator and UI metadata.
- `Operators/Lib/io/audio/AudioPlayerUtils.cs` — shared operator audio utilities.
- `Operators/Lib/io/audio/AudioToneGenerator.cs` (+ `.t3`/`.t3ui`) — tone generator operator and UI.
- `Operators/Lib/io/audio/SpatialAudioPlayer.cs` (+ `.t3`/`.t3ui`) — spatial audio operator and UI metadata.
- `Operators/Lib/io/audio/SpatialAudioPlayerGizmo.cs` (+ `.t3`/`.t3ui`) — spatial audio player gizmo visualization.
- `Operators/Lib/io/audio/_/GetAllSpatialAudioPlayers.cs` (+ `.t3`/`.t3ui`) — query operator for spatial players.

## Modified

### Core Audio Files
- `Core/Audio/AudioAnalysis.cs` — now delegates to `AudioAnalysisContext.Default` for backwards compatibility.
- `Core/Audio/AudioEngine.cs` — central audio API changes for playback/update/export integration.
- `Core/Audio/AudioRendering.cs` — export/mixdown improvements and buffer reuse notes.
- `Core/Audio/BeatSynchronizer.cs` — beat detection / timing adjustments.
- `Core/Audio/WasapiAudioInput.cs` — Wasapi input adjustments.
- `Core/Audio/WaveFormProcessing.cs` — now delegates to `AudioAnalysisContext.Default` for backwards compatibility.

### Editor Files
- `Editor/Gui/Audio/AudioImageFactory.cs` — audio image factory updates.
- `Editor/Gui/Audio/AudioImageGenerator.cs` — audio image generation tweaks.
- `Editor/Gui/InputUi/VectorInputs/Vector4InputUi.cs` — vector4 input UI changes.
- `Editor/Gui/Interaction/Timing/PlaybackUtils.cs` — playback timing helpers updated.
- `Editor/Gui/OpUis/OpUi.cs` — operator UI adjustments.
- `Editor/Gui/UiHelpers/UserSettings.cs` — user settings persistence/UX changes.
- `Editor/Gui/Windows/RenderExport/RenderAudioInfo.cs` — render audio info updates.
- `Editor/Gui/Windows/RenderExport/RenderProcess.cs` — render/export process changes.
- `Editor/Gui/Windows/RenderExport/RenderTiming.cs` — render timing adjustments.
- `Editor/Gui/Windows/SettingsWindow.cs` — settings window updated to include audio panel.
- `Editor/Gui/Windows/TimeLine/PlaybackSettingsPopup.cs` — timeline playback settings tweaks.
- `Editor/Gui/Windows/TimeLine/TimeControls.cs` — timeline controls updated.
- `Editor/Program.cs` — editor startup changes to include audio initialization.

### Logging Files
- `Logging/Log.cs` — added `Log.Gated` API for category-based debug logging.

## Renamed

- `Core/Audio/AudioClipDefinition.cs` → `Core/Audio/SoundtrackClipDefinition.cs` — renamed soundtrack clip definition.
- `Core/Audio/AudioClipStream.cs` → `Core/Audio/SoundtrackClipStream.cs` — renamed soundtrack clip stream.

