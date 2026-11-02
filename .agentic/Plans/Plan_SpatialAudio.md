# Plan: Spatial audio for installations and venues

**Status:** Proposed, 2026-11-02. Nothing built. Starts after [`Plan_ReplaceBass.md`](Plan_ReplaceBass.md) lands.

## Goal

Play TiXL's sound live over many speakers from inside the editor and exported players: a 6-8 speaker
installation with a sub, and a venue with 50-64 speakers such as a planetarium dome. A show is composed on a small
setup and moved to the venue by swapping the speaker layout, without changing the project.

**Live is the point.** Rendered stems are a useful handover, not the main route.

## Non-goals

TiXL places and moves sounds. It does not become a DAW. Room simulation beyond a simple reverb send, convolution,
wave field synthesis and large research arrays stay in SuperCollider, Reaper or SPAT, reached through `[OscOutput]`.

We don't integrate IRCAM's spat~. It is Max-based, and its commercial terms look restrictive, which is the kind of
dependency the BASS replacement removes. Use it and XP4L as feature references only.

## How it works in a venue

There are three routes. The first two are live.

| Route | Who pans | On the cable | Needs from the venue |
|---|---|---|---|
| **Speaker feeds** | TiXL | One channel per speaker | Layout data, access to its routing |
| **Objects** | Venue renderer | One channel per sound, plus positions | Renderer access, a shared position protocol |
| **Stems** | Venue renderer | Files plus position data | Playback only |

Nobody brings a 64-channel card. Channels travel over one network cable (Dante, AES67) or one MADI cable, using a
software soundcard or a small interface.

**Standards**

- **Audio transport:** AES67 and ST 2110-30 are the open common ground. Dante is proprietary and widespread, with
  AES67 and ST 2110-30 modes for interoperability. Audinate's guide describes the AES67 mode as 8 channels per
  flow, so check how that scales.
- **Positions:** ADM-OSC is open, carries object positions over OSC, and is supported by SPAT Revolution and L-ISA,
  partly by KLANG. A validator exists (the `adm-osc` Python package).

## Current state

The engine is stereo throughout, though miniaudio itself is not the limit (254 channels; WASAPI, CoreAudio, ALSA,
PulseAudio and JACK backends, no ASIO).

**Stereo assumptions to remove, all in `Native/TixlAudio/tixl_audio.c` unless noted:**

- Engine and device setup: `ta_create` and `ta_open_device` set two channels.
- `ta_stereo_node_init`, used by taps, buses and the echo. The echo's delay line is stereo too.
- `ta_tap_ring_write` folds the first two channels to mono, so it would ignore all other speakers.
- `outputStereo` and `ta_output_read_stereo`, which feed waveform displays.
- `ta_get_info` reports two channels.
- `Core/Audio/AudioRendering.cs` assumes stereo for export.
- `ta_capture_start` opens two channels and `captureRing` is stereo, so audio input is limited to one stereo pair.

**No native reverb.** The `[AudioReverb]` operator exists, but the engine has no reverb implementation, so assume it
produces no reverb on this branch. Effects are low priority in the BASS replacement.

**miniaudio's spatializer can't serve this.** It pans by direction from a central listener using fixed positions,
so it knows no custom speaker positions.

**Channel maps:** miniaudio names only `AUX_0` to `AUX_31`, so a discrete map for 64 channels needs checking. Device
setup must also stop miniaudio from remixing channels by speaker name.

## Design

**Scenes describe the room, not speakers.** Sources hold positions in room space. Nothing in a project may refer to a
speaker index, so a layout swap can't break it. A mapping step (scale, offset, rotation) fits a scene to another room.

**Speaker layout is separate from the project's content.** It can be chosen per machine or venue, and holds per
speaker: position, output channel, gain trim, delay. Swapping one must take seconds. Output Setup already has floor
plans, so speakers should share that room model, giving image and sound one coordinate system.

**Stereo stays the default.** Existing projects keep their stereo path. Multichannel is opt-in, and existing
`[PlaySpatialAudioSample]` projects must sound the same on stereo output.

**Panner as a native node:** mono in, N out, in C on the audio thread, with the method swappable.

| Method | Use | When |
|---|---|---|
| DBAP | Irregular layouts, walk-through audience, no sweet spot | First |
| VBAP | Sharper placement | Later |
| Ambisonics | Central audience, regular rings and domes, ambisonic material | Later, as an extra bus with an encoder per source and a decoder to the same layout |

Stereo sources go to a chosen speaker pair or spread evenly.

**Subwoofer:** a summed feed with a low-pass around 80-120 Hz on its own output, with an optional matching high-pass
for the main speakers. Crossover details are decided when tuned by ear.

**Protection:** a limiter on each output, because a venue's amps are not ours to damage.

**Analysis** stays on the existing mono tap, averaging all channels. Existing projects are tuned to its levels, so
stereo output must read exactly as before.

## Phases

### Phase 0: Multichannel spike

In `Spikes/MiniaudioSpike`, send a test signal to each channel in turn. This decides whether ASIO is needed.

- A real 8+ channel interface on Windows, through WASAPI shared and exclusive mode.
- Dante Virtual Soundcard's WDM device on Windows, at 64 channels.
- Some interfaces split outputs into stereo devices. Note which.
- JACK and PipeWire on Linux, CoreAudio on macOS.
- Discrete channel maps above 32 channels.
- Mixing cost: many DBAP sources into 64 channels, measured on the audio thread beside a real render load.

**Done when** each channel is confirmed individually on real hardware, and there is a recorded yes or no on ASIO.

### Phase 1: Multichannel engine

- Configurable channel count and output device, passed straight through.
- Native DBAP panner, per-speaker gain and delay, sub feed, output limiters.
- Remove the stereo assumptions listed above.
- Per-speaker meters and a test signal that steps through the speakers.

**Done when** a 6-8 speaker and sub setup works live, and stereo projects are unchanged. Extend
`Core.Tests/NativeAudioEngineTests.cs`, which runs offline, with per-channel assertions.

### Phase 2: Layouts and sources

- Speaker layouts in Output Setup, with presets and fast swapping.
- Room mapping for moving a scene between rooms.
- Any source or bus can be spatial, including live input and video audio. Today only file-based mono sources can.
- An operator controlling many sources together, for swarms. The engine must handle that many panners.
- Multichannel audio capture, with each input channel usable as its own spatial source. This lets external synths
  such as SuperCollider send separate voices into TiXL through an interface or a virtual device (BlackHole on macOS,
  JACK or PipeWire on Linux). Analysis and recording of today's stereo input stay unchanged.

**Done when** a scene made on 7 speakers plays on a simulated 64-speaker layout after only a layout swap, and separate
voices from an external synth move independently in the scene.

### Phase 3: Monitoring and export

- Binaural preview: each speaker as a virtual one, filtered with an HRIR for its direction and summed to stereo. It
  checks the real layout on headphones. Needs a small convolver and an HRTF dataset whose licence allows shipping.
- Optional rotation input on the preview, for head tracking.
- Multichannel WAV export.

### Phase 4: Object mode

- Skip the panner, one output channel per sound, and stream positions over ADM-OSC with `[OscOutput]`'s stack.
- Stem export: one WAV per object plus position data. The file format needs the interface audit below.
- LTC timecode in or out, if a venue actually asks for it.

**Done when** SPAT Revolution or L-ISA follows positions live, and the Python validator passes.

### Phase 5: Later

- Native reverb, which reverb sends depend on. It must work with more than two channels, and it should also make
  `[AudioReverb]` work in stereo.
- Reverb sends for rooms, with controls in perceptual terms where that helps.
- VBAP.
- Ambisonics, compared with DBAP by ear in the same room.

## Risks

- **Windows without ASIO.** miniaudio has no ASIO backend, and many pro interfaces and Dante setups expect it. WASAPI
  shared mode only exposes what the Windows speaker configuration allows. A custom backend is possible, but check the
  ASIO SDK's licence first, given why BASS is being removed.
- **Dante on Linux is unconfirmed.** Assume Windows or macOS for these events until checked. AES67 on Linux may be an
  alternative.
- **Domes.** DBAP may suit a seated audience under speaker rings less well than a walk-through room, which could move
  VBAP or ambisonics forward. Needs listening tests.
- **Load.** A 64-channel mix shares the machine with rendering. Phase 0 measures this.
- **Clock drift.** Never combine several audio devices without a shared clock.
- **Venue access.** A venue may refuse direct speaker feeds or live guest input, leaving only objects or stems.

## Open questions

**For a venue, such as the Zeiss-Großplanetarium Berlin:**

Its site lists Fraunhofer's Spatial Sound Wave with 49 speakers and 4 subwoofers. An older trade case study names a
Shure/Fraunhofer system with Dante Virtual Soundcard for up to 32 channels. Fraunhofer's Jena install lists 32 inputs,
64 outputs, MADI and LTC, which may differ in Berlin.

1. Which renderer is installed now, and how many objects does it take?
2. Does it take live input from guests, and over Dante, MADI or something else?
3. Can a guest feed the speakers directly, bypassing the renderer?
4. How does it receive positions: OSC, ADM-OSC, or only its own software? No public documentation found.
5. Timecode, sample rate and clock requirements?
6. Can we get the speaker positions, and how much rehearsal time in the dome?

**For Monocolor,** who prepared a live show on 7 speakers and then "just plugged in", reportedly with XP4L (from the
user's conversation, not confirmed in public sources): how did the audio connect, were the speakers driven directly,
and how much rehearsal did it take?

## Interface audit before shipping

Before the layout format, the position file or operator parameters land on user disks, run the audit from
`AGENT_INSTRUCTIONS.md`. Layouts and position files need a version field and optional metadata. Reference speakers by
stable id, not index, so re-patching channels doesn't invalidate anything.

## Docs and tests

Each user-visible phase updates `.help/`, the release notes and a set in `.tests-manual/`. A venue checklist based on
the questions above belongs in `.help/`.

## References

- [XP4L overview](https://forum.ircam.fr/article/detail/xp4l-a-flexible-spatial-sound-system-for-ableton-live/) and [video](https://www.youtube.com/watch?v=qE9q1Ee2zVY)
- [Spat's panning module](https://www.soundingfuture.com/en/article/ircam-spat-swiss-army-knife-immersive-audio-creation-and-performance)
- [ADM-OSC specification](https://redirect.github.com/eviau-sat/ADM-OSC) and [its background](https://audioxpress.com/news/flux-and-l-acoustics-promote-adm-osc-standardization-for-object-based-audio)
- [Audinate: AES67 and ST 2110-30](https://dev.audinate.com/GA/dante-controller/userguide/webhelp/content/aes67_and_smpte_st2110-30_audio.htm)
- [Planetarium Berlin technology](https://www.planetarium.berlin/en/zeiss-grossplanetarium/technik-zgp), [trade case study](https://www.professional-system.de/case-studies/der-sound-in-der-sternenkuppel-im-zeiss-grossplanetarium-berlin/), [Fraunhofer Jena spec](https://www.idmt.fraunhofer.de/content/dam/idmt/documents/IL/zeiss_planetarium_jena_en.pdf)
