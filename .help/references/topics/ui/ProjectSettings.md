The per-project settings that travel with a composition — timing, audio, video proxies, recording sources, and export behavior.

Settings live on a composition and are inherited by everything inside it. The checkbox at the top ("Specify settings for …") controls whether the current composition defines its own settings or inherits them from a parent.

### Timing

**Project Setup** decides how the project runs:

- **Animation** — for repeatable, timeline-driven pieces with a known duration and keyframe animation.
- **Live / Interactive** — for live performances, VJ sets and installations.

Because all timing in TiXL runs in musical bars rather than seconds, the **BPM** rate controls the animation speed of the whole project — getting it right keeps procedural animation locked to the beat. **Timeline Display** chooses whether the [ui:Timeline|timeline] ruler reads in bars, seconds, or frames.

In Live / Interactive mode, **Sync Mode** picks the clock source:

- **Timeline** — the project still runs on its own timeline at a fixed BPM-rate.
- **Tapping** — tap the Sync button (or a hotkey) on every beat to set the tempo live. With **audio beat lock** enabled, the editor listens for transients in the audio input and continuously slides the playback speed to stay locked onto the incoming beat. **Beat Sync Offset** shifts the phase slightly — useful when video-processing devices delay the visual output.

### Audio

**Main Volume** sets the overall audio output level of the project, and the **Resync Threshold** controls how much audio playback may drift from the animation before it is resynced (0.02–0.05 s is a normal range).

**Audio Analysis and Reactivity** configures the signal that drives audio-reactive operators like [AudioReaction]: the **Input Device** (leave it empty to use the machine's default device, which is stored per machine so shared projects work everywhere), a **Gain** to adjust varying input levels, and a **Decay** that shapes how quickly reactions fall off. The level meter shows the incoming signal after gain.

Since TiXL 4.3, audio lives on the timeline as ordinary operators: drag an audio file from the Asset Library or your file explorer onto the timeline and it becomes an [AudioClip] — a clip you can drag, trim, split, loop and mute like any other, and a project can have many of them playing in parallel. One clip can be the **main soundtrack**: it renders as the full-width image behind the timeline, drives audio-reactive operators, and defines the duration of exported executables. Use **Create Soundtrack** here (or **Select and focus Main Soundtrack** once one exists), or right-click any audio clip in the timeline and choose **Set as main soundtrack**. The main soundtrack always spans its full source content, and its clip block is hidden from the timeline layers — the background image is its representation. To reposition it or turn it back into a regular clip, set the operator's **Display** parameter back to **Clip**.

For routing and mixing, wire a clip's `AudioReference` output into an [AudioBus] — directly, or through [CombineAudio] groups and effects like [AudioReverb], [AudioEcho] and [AudioCompressor]. An [AudioLevel] tap measures the signal anywhere in that chain, e.g. to duck music under a voice-over with [DuckAudioLevel]. With **AutoCollectClips** enabled on a bus or group, unwired clips join it implicitly. Everything you hear this way is also what renders into exported videos and executables.

### Video Proxies

Proxies are downscaled, fast-seeking copies of video clips used for preview — rendering to file always uses the full-resolution source. Generate them from a video operator's context menu, then enable **Use proxies for preview** to scrub with them. Pick a **Proxy Format** (ProRes is a balanced default; Hap variants scrub fastest at a larger file size) and a **Proxy Resolution** as a fraction of the source size. The **Storage** section shows how much disk the generated proxies use, per project and across all projects, with a one-click cleanup — sources are untouched and proxies can be regenerated at any time.

### Recording

Chooses which sources the timeline's Record button captures: **Capture Audio** records the active input device, and **Capture IO** records incoming MIDI and / or OSC events into a data file alongside the audio clip, so a live take can later be replayed and refined.

### Export

Defaults for projects exported as standalone executables: the **Window Mode** the executable starts in, and whether viewers get **keyboard playback control** (cursor keys to skip, space to pause). Exporting itself happens through the [ui:PlayerExporter|export dialog].
