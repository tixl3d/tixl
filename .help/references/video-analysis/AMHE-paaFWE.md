---
video: AMHE-paaFWE
type: tutorial
date: 2022-07-20
title: Tooll 3 Tip#016 - Adjusting Soundtrack and BPM-Rate
duration: 0:06:42
focusesOn: [ui:ProjectSettings]
---

A tip walkthrough explaining why all time in Tooll runs in bars rather than seconds, how to add a soundtrack to a project and dial in its BPM rate so procedural animation stays synced.

## Mentions
- 0:35→0:53 [Time] · explained · scripted · Example · 88% — Drop one in to read back the playhead's current value and confirm it keeps advancing even while playback is paused, as long as idle motion is on.
- 0:45→1:08 [ui:IdleMotion] · explained · scripted · Concept · 82% — Why the graph keeps evaluating and animating while playback is stopped; essential for previewing procedural animation without hitting play, and can be toggled off to freeze the current frame.
- 1:13→2:06 [Time] [ui:Timeline] · in-depth · scripted · Parameters · 85% — Switch the time operator's mode to flip the readout between seconds and bars; the underlying clock is always driven in bars, so one second equals only half a bar at 120 BPM — the source of the "off by half" surprise when reading seconds.
- 2:06→2:31 [ui:ProjectSettings] · explained · scripted · Concept · 80% — Because time is measured in bars, changing a project's BPM rate live re-times every procedural animation at once and keeps it locked to the soundtrack.
- 2:42→3:31 [ui:ProjectPanel] · passing · scripted · Concept · 70% — The dashboard is a desktop-like home holding all projects rather than a project itself; to get soundtrack and BPM settings you must first turn your graph into a real project.
- 3:31→4:11 [ui:ProjectSettings] · in-depth · scripted · Example · 88% — Open the playback settings on a composition and use the file picker to attach an audio file; a waveform thumbnail is generated in the background and appears once ready.
- 4:11→4:48 [ui:ProjectSettings] · in-depth · scripted · Parameters · 87% — Slide the BPM control until beats line up with the audio when you don't know the exact tempo, and use the soundtrack offset to skip silence at the start of the file.
- 4:48→5:24 [ui:ProjectSettings] · explained · scripted · Gotcha · 78% — Soundtrack and tempo are inherited from a parent operator, so diving deeper keeps the same settings while jumping out to a different project swaps in its own — each project carries independent BPM settings.
- 5:29→5:46 [ui:PlayerExporter] · passing · scripted · Tip · 72% — Exporting the composition as a standalone executable bundles its resources and preserves the configured BPM rate so the build plays back at the correct tempo.
- 5:46→6:09 [ui:Timeline] · explained · scripted · Tip · 80% — With the BPM set correctly, working in bars mode lets the playhead snap cleanly to beats; other readout modes stay available when a different unit is easier to read.
