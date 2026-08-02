# Project Audio

Output level, audio-reactive analysis, and the project soundtrack.

- **Resync Threshold** — how far audio playback may drift from the animation before it is resynced (0.02–0.05 s is a normal range).
- **Main Volume** — the overall audio output level of the project.
- **Input Device / Gain / Decay** — the signal driving audio-reactive operators like [AudioReaction]. Leave the device on **Default Audio Input** to use the machine's default, which is stored per machine so shared projects stay portable.
- **Project Soundtrack** — soundtracks are [AudioClip] operators on the timeline; drag an audio file onto the timeline to create one. The clip with **Display** set to **Background Image** is the main soundtrack: it renders as the image behind the timeline, drives audio-reactive operators, and defines the exported duration. To reposition it, set its Display back to **Clip**.

Full documentation of the audio pipeline — clips, routing, effects, ducking, reactivity: [help.tixl.app/using/Audio](https://help.tixl.app/using/Audio/)
