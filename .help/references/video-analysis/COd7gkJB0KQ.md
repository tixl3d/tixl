---
video: COd7gkJB0KQ
type: tutorial
date: 2022-09-10
title: Tooll 3 Tip#021 - Hacking the render Time
duration: 0:04:14
focusesOn: [ui:TimeOverrides], [SetTime], [Time], [GetFrameSpeedFactor]
---

A short tip on overriding the time that drives an animation: wrap a rendered texture in a command-time operator and feed it a custom time signal (a sine, a remapped random, a beat) to scrub, loop, or jump regions of a timeline non-linearly.

## Mentions
- 0:31→0:51 [ui:OutputWindow] · explained · scripted · Concept · 80% — The render output is driven by a single global time value (in bars); the current playhead position is what each frame samples to decide what to draw.
- 0:40→0:51 [Time] · explained · scripted · Concept · 70% — Reads the global playhead position (measured in bars) that every frame samples by default; tap it as the source signal you then reshape before substituting a new time downstream.
- 0:54→1:23 [SetTime] · in-depth · scripted · Concept · 75% — Wrap a rendered texture in this operator to swap the time feeding that sub-graph: pin it and its time input starts at zero, so scrubbing or piping in any float value re-renders the contents at whatever moment you supply instead of the live playhead.
- 0:54→1:23 [ui:TimeOverrides] · in-depth · scripted · Concept · 75% — How to locally override the time feeding a sub-graph: render content into a texture, wrap it in a command-time operator, then drive that operator's time input yourself instead of the global playhead — pinning it shows the overridden frame.
- 3:14→3:20 [GetFrameSpeedFactor] · passing · scripted · Concept · 50% — Not shown directly, but the same render-time hacking applies: read the per-frame speed/playback factor to scale a driver so an overridden time advances in step with the project's actual playback rate rather than a fixed increment.
- 1:34→2:07 [Sin] · explained · scripted · Example · 85% — Pipe the original requested time through a sine, offsetting and raising the amplitude, then feed the result back as the new time to make the animation ping-pong forward and backward within a range.
- 2:37→2:45 [AnimValue] [Random] · passing · scripted · Example · 65% — Driving an overridden time with an animated or randomized value so different sections of an animation can be triggered on demand, e.g. for an audio-reactive VJ set.
- 2:49→3:04 [Remap] · explained · scripted · Tip · 80% — Constrain a chaotic driver (a random or beat signal) to a usable window by remapping it onto a specific region of the timeline, so jumps land inside a chosen ~10-second range instead of anywhere.
- 3:26→3:46 [ui:TimeOverrides] · explained · scripted · Gotcha · 75% — Mixing keyframe animation with a manually overridden time gets very confusing fast, because the keyframes are evaluated against the substituted time rather than the real playhead — keep the two techniques separate.
