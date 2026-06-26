---
video: 854OSxXzMDM
type: tutorial
date: 2022-07-12
title: Tooll 3 Tip#009 - Animating String Parts
duration: 0:03:03
focusesOn: [PickStringPart]
---

A short tip-of-the-day walkthrough showing how to split a text into lines/words/characters and step through the fragments to animate text, then pointing to a simpler random-string shortcut.

## Mentions
- 0:00→0:30 [Text] · passing · scripted · Example · 70% — Paste multi-line content in and switch its display to resizable to drag the text block bigger via the corner handle.
- 0:30→1:00 [PickStringPart] · in-depth · scripted · Parameters · 88% — Choose to break the source into lines, words, characters, or sentences, then read out one slice — the core operator for per-word or per-character text animation.
- 1:00→1:30 [PickStringPart] · explained · scripted · Parameters · 85% — Fragment-start picks which slice and fragment-count picks how many; raising the count past the end wraps around, and a total-count output lets you remap an index cleanly across all slices.
- 1:30→1:54 [FloatToInt] [TriggerAnim] · explained · scripted · Example · 78% — Drive the fragment-start from a rising counter converted to an int so the readout steps word-by-word; raising the source frequency speeds the stepping.
- 2:01→2:05 [TriggerAnim] · passing · scripted · Tip · 60% — Match its end value to its start value when you want it to type-on from a fixed point rather than ramping.
- 2:11→2:18 [ChangeCase] · passing · scripted · Tip · 65% — One of the string operators you can chain after slicing to further transform the picked text, e.g. forcing upper/lower case.
- 2:21→2:50 [AnimRandomString] · explained · scripted · Comparison · 80% — A one-node shortcut that does the same slice-and-step animation as wiring up [PickStringPart] by hand, and ships with ready-made word lists (colors, names); pairs well with beat detection.
