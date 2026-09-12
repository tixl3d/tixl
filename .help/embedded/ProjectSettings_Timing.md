# Timing

How the project runs and how time is measured. These settings travel with the composition.

- **Project Setup** — **Animation** for repeatable, timeline-driven pieces with keyframes and a known duration; **Live / Interactive** for VJ sets and installations.
- **BPM** — all timing in TiXL runs in musical bars, so the BPM rate sets the animation speed of the whole project.
- **Timeline Display** — show the timeline ruler in bars, seconds, or frames.
- **Sync Mode** (Live only) — **Timeline** keeps a fixed BPM-rate; **Tapping** sets the tempo live from your taps. With **audio beat lock** enabled, playback continuously locks onto the audio input. **Beat Lock Source** picks the analysis: **Onset Detection** follows transients and needs a tapped resync to find the bar; **Phase Model** uses DanceAi, a small neural network (16 MB) self-trained by Felix Niemeyer for exactly one job: estimating bar phase and tempo of an audio stream. It runs locally in the background, costs about 40 % of one CPU core while selected, needs no tapping, and reports its confidence. **Beat Sync Offset** shifts the phase to compensate for delayed video output.

Hover the **(?)** next to any field for a short explanation.
