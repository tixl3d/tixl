---
video: COd7gkJB0KQ
type: tutorial
date: 2022-09-10
title: Tooll 3 Tip#021 - Hacking the render Time
duration: 0:04:14
focusesOn: [ui:TimeOverrides]
---

A short tip on overriding the time that drives an animation: wrap a rendered texture in a command-time operator and feed it a custom time signal (a sine, a remapped random, a beat) to scrub, loop, or jump regions of a timeline non-linearly.

## Mentions
- 0:31→0:51 [ui:OutputWindow] · explained · scripted · Concept · 80% — The render output is driven by a single global time value (in bars); the current playhead position is what each frame samples to decide what to draw.
- 0:54→1:23 [ui:TimeOverrides] · in-depth · scripted · Concept · 75% — How to locally override the time feeding a sub-graph: render content into a texture, wrap it in a command-time operator, then drive that operator's time input yourself instead of the global playhead — pinning it shows the overridden frame.
- 1:34→2:07 [Sin] · explained · scripted · Example · 85% — Pipe the original requested time through a sine, offsetting and raising the amplitude, then feed the result back as the new time to make the animation ping-pong forward and backward within a range.
- 2:37→2:45 [AnimValue] [Random] · passing · scripted · Example · 65% — Driving an overridden time with an animated or randomized value so different sections of an animation can be triggered on demand, e.g. for an audio-reactive VJ set.
- 2:49→3:04 [Remap] · explained · scripted · Tip · 80% — Constrain a chaotic driver (a random or beat signal) to a usable window by remapping it onto a specific region of the timeline, so jumps land inside a chosen ~10-second range instead of anywhere.
- 3:26→3:46 [ui:TimeOverrides] · explained · scripted · Gotcha · 75% — Mixing keyframe animation with a manually overridden time gets very confusing fast, because the keyframes are evaluated against the substituted time rather than the real playhead — keep the two techniques separate.
