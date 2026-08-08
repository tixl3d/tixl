# Audio

How audio works in TiXL: clips on the timeline, the routing and mixing graph, effects, audio-reactive visuals, and what ends up in your exports. Since TiXL 4.3, all of this is built from ordinary operators — there is no separate audio configuration beyond a few project settings.

## Audio clips on the timeline

Drag an audio file (mp3, wav, ogg) from the Asset Library or your file explorer onto the timeline and it becomes an [AudioClip] operator — a clip you can move, trim, split, loop, mute and delete like any other timeline clip. A project can have many audio clips playing in parallel, across as many layers as you need.

- **AutoPlay** (on by default) makes the clip play whenever the playhead is inside it — no wiring required.
- **Trimming** the clip's edges plays only that part of the file; dragging the start before the file's beginning leaves that stretch silent.
- **Loop** repeats the trimmed source window for the length of the clip. Repeat boundaries show as thin lines in the clip body.
- **Mute** silences the clip; muted clips render faded in the timeline.
- **Style** picks the image drawn in the clip body: a frequency **Spectrum**, a peak **Waveform**, or a smoothed **Volume Level**.
- Renaming the operator shows the new name in quotes on the clip; the file name stays visible in the tooltip.

Splitting a clip (`Shift+X` at the playhead) keeps both halves playing seamlessly, and reconnects the new clip to whatever the original was wired into.

## The main soundtrack

One clip can be designated the **main soundtrack**. It renders as the full-width image behind the timeline, drives audio-reactive operators, and defines the duration of exported executables.

To designate it, right-click an audio clip and choose **Set as Main Soundtrack**, or use **Create Soundtrack** in Project Settings → Audio. Technically the designation is just the clip's **Display** parameter set to **Background Image** — so it travels with the operator, not with any hidden project state.

The main soundtrack always spans its full source content, and its clip block is hidden from the timeline layers: the background image is its representation. To reposition it on the timeline or turn it back into a regular clip, set the operator's **Display** parameter back to **Clip** — Project Settings → Audio → **Select and focus Main Soundtrack** jumps you to the operator.

## Routing and mixing

By default, clips play on their own and you never see a wire. For mixing, grouping and effects, use the audio graph:

- Every [AudioClip] has an **AudioReference** output. Wire it into an [AudioBus] and the bus takes over the clip's playback level and mixing. The bus is the sink of the graph — it owns a master **Volume** and reports a **Level** you can meter.
- [CombineAudio] groups several sources under a shared group volume before they reach the bus. Groups can nest.
- Effects — [AudioReverb], [AudioEcho], [AudioCompressor] — are inserts: everything flowing *through* them is processed. Chain them by wiring one into the next.
- [AudioToneGenerator] produces test tones and synth drones directly in the graph; [PlayAudioSample] triggers one-shot samples.

With **AutoCollectClips** enabled on a bus or a group, unwired sibling audio clips join it implicitly — useful when you want a whole composition's clips to share a volume, a meter, or an effect without wiring each one. Use only one auto-collecting operator per composition.

The bus must be evaluated to play — wire it into your render chain or pin it to an output view.

## Metering and ducking

An [AudioLevel] tap measures the signal at the point where it sits in the chain — wire it **inline** (source → tap → bus), not as a side branch, so it can meter timeline clips. Its level output drives anything: lamp brightness, scale, or a [DuckAudioLevel], which lowers one signal while another is loud — the classic voice-over-over-music setup:

1. Route the music through a [DuckAudioLevel].
2. Tap the voice-over group with an [AudioLevel] and wire its level into the duck's control input.
3. Adjust threshold and amount until the music breathes with the voice.

## Audio-reactive visuals

[AudioReaction] reacts to the project's audio analysis — beats, bass, highs — and outputs values to drive parameters. What it listens to depends on the project setup (Project Settings → Timing):

- **Animation** projects analyze the **main soundtrack**, frame-accurately — also while rendering videos, so exported reactivity matches playback.
- **Live / Interactive** projects analyze the **audio input device** configured in Project Settings → Audio, with **Gain** to adjust varying input levels and **Decay** to shape how quickly reactions fall off.

## Export

What you hear is what you get: timeline clips and everything routed through the audio graph — including group volumes, effects and ducking — render into exported videos and executables. The main soundtrack defines an executable's play duration.

## See also

- [Timeline](Timeline.md) — clips, keyframes, time warping.
- [TiXL for VJ and live performances](LivePerformances.md) — tap tempo, beat lock, live input.
- [Exporting videos](ExportVideos.md) and [executables](ExportExecutables.md).
