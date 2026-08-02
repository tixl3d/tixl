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
