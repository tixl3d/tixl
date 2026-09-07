---
id: audio-graph-routing
title: Audio Graph — routing, mixing and effects
scope: audio
tags: [user, essential, hardware]
added: 2026-08-01
added-in-version: 4.3
prerequisites:
  - An empty project is open and audio output is audible.
---

Covers the audio-processing graph: routing sources through an [AudioBus],
grouping with [CombineAudio], effect inserts ([AudioReverb], [AudioEcho],
[AudioCompressor]) including chained effects, and the rules for when audio
is audible (evaluated bus = sound; disconnected = silence).

## Step: A tone plays through a bus

**Action:**
Add an [AudioToneGenerator] and an [AudioBus]. Wire the tone generator's
`AudioReference` output into the bus, and the bus's `Result` into your render
chain (or pin the bus to an output window). Set the tone's `Trigger` to true.

**Expected:**
- The tone is audible while `Trigger` is on.
- Changing `Frequency` and `Volume` takes effect immediately.
- With `Trigger` off, the tone fades out following the envelope's release.

## Step: Disconnecting silences immediately

**Action:**
While the tone is sounding, delete the connection between the tone generator
and the bus.

**Expected:**
- The audio stops immediately — no release tail, no lingering sound.
- Reconnecting stays silent until `Trigger` fires again (or keeps sounding if
  `Trigger` is held true in Gate mode).

## Step: Group volume folds

**Action:**
Insert a [CombineAudio] between two tone generators and the bus. Adjust the
[CombineAudio]'s `Volume`.

**Expected:**
- Both tones scale together with the group volume.
- Each tone's own `Volume` still works independently on top of it.

## Step: Reverb insert

**Action:**
Replace the [CombineAudio] with an [AudioReverb]. Trigger a short tone and
adjust `Mix` and `Time`.

**Expected:**
- The tone rings out with a reverb tail; `Time` changes the decay length
  (up to its 3-second maximum), `Mix` the wet amount.
- Parameter changes are audible while the sound plays — no rewiring needed.

## Step: Echo insert

**Action:**
Swap the reverb for an [AudioEcho]. Trigger a short tone and adjust `Delay`,
`Feedback` and `PingPong`.

**Expected:**
- Distinct repeats follow the tone; `Delay` sets their spacing, `Feedback`
  how many repeats are heard.
- With `PingPong` on, repeats alternate between left and right.

## Step: Compressor insert

**Action:**
Route a loud triggered tone through an [AudioCompressor] into the bus. Set
`Threshold` around 0.2 and `Ratio` around 10, then trigger the tone; afterwards
raise `MakeupGainDb`.

**Expected:**
- The tone's sustain audibly flattens compared to the un-compressed signal.
- Raising `MakeupGainDb` brings the compressed signal's loudness back up
  ("breathing" on the tail is normal at strong settings).
- `Attack` and `Release` change how quickly the compression clamps and lets go.

## Step: Chained effects

**Action:**
Wire the tone through [AudioEcho] *into* [AudioReverb], then into the bus.

**Expected:**
- Both effects are audible at once: echo repeats that each carry reverb.
- Both ops' parameters remain live.
- Swapping the order (reverb into echo) audibly changes the character.

## Step: Effect tail fades on unwire

**Action:**
While an echo or reverb tail is still sounding, disconnect the effect op
from the bus.

**Expected:**
- The tail fades out quickly (~half a second) instead of cutting off hard.

## Step: Handoff between two buses

**Action:**
Add a second [AudioBus] and wire the same tone chain into it. Pin one bus to
an output window so only it is evaluated, then switch the pinning to the
other bus.

**Expected:**
- The sound follows whichever bus is currently evaluated — switching pins
  moves the audio without permanent silence.
- (Known limitation: if *both* buses are evaluated in the same frame, they
  contend for the source and the console shows repeated routing messages —
  multi-send is not supported yet.)

## Step: A sampler plays unrouted, exactly as before

**Action:**
Add a [PlayAudioSample], point `AudioFile` at a short sample, and leave its
`AudioReference` output unconnected. Trigger `PlayAudio` a few times, adjusting
`Volume`, `Mute` and `Panning`.

**Expected:**
- The sample plays on every trigger, as it did before the graph existed.
- `Volume`, `Mute` and `Panning` all take effect immediately.
- No routing messages appear in the console — nothing in the graph has claimed it.

## Step: Wiring only the reference is enough to play

**Action:**
Take a [PlayAudioSample] whose `Result` output is connected to **nothing**, and
wire only its `AudioReference` into an [AudioBus] (directly or through a
[CombineAudio]). Make sure the bus is evaluated — pinned to an output window or
wired into the render chain. Trigger `PlayAudio`.

**Expected:**
- The sample plays. Evaluation by the bus is what drives it; no command
  connection is needed.
- Every trigger fires exactly once — no double-triggering or skipped notes, and
  with `Use Envelope` on, the attack fires once per trigger rather than twice.

## Step: Routing a sampler into a bus moves its level to the graph

**Action:**
Wire the [PlayAudioSample]'s `AudioReference` into an [AudioBus] and trigger it
again. Then set the bus `Volume` to 0.3, and separately set the sampler's own
`Volume` to 0.3 with the bus back at 1.0.

**Expected:**
- The sample still plays on trigger, now through the bus.
- Both volumes scale it, and they multiply — the sampler's own `Volume` keeps
  working rather than being overridden or fighting the bus (no flicker,
  pumping, or level jumping between two values on alternating frames).
- `Panning` still works while routed; panning is not a graph parameter.

## Step: Envelope and effects apply to a routed sampler

**Action:**
With the sampler routed, enable `UseEnvelope` and give it a slow attack and
release. Insert an [AudioReverb] between the sampler and the bus.

**Expected:**
- The attack and release are audible, and the reverb follows them — the envelope
  reaches the graph as gain rather than being applied behind it.
- A [DuckAudioLevel] driven from another source ducks the sampler as it would
  any other graph source.

## Step: Un-routing hands the sampler back

**Action:**
While the sampler is routed and audible, delete the connection to the bus.
Trigger it again. Then delete the [AudioBus] entirely while a sample is playing.

**Expected:**
- After unwiring, the sampler keeps playing on trigger at its own `Volume` —
  it returns to the operator mixer rather than going permanently silent.
- Deleting the bus mid-sample does the same; no stuck silence, and no repeated
  routing warnings in the console.
