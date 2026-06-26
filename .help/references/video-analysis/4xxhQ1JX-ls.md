---
video: 4xxhQ1JX-ls
type: meetup
title: "TiXL Update: Drawing with OSC Input"
duration: 0:03:51
focusesOn: [OscInput]
---

How to drive live visuals from a phone or tablet over OSC: [OscInput] auto-detects the address of any control you touch (the built-in teach mode), multiple inputs on the same port share one connection for free, and its float output can be split into X/Y/pressure to paint feedback trails whose color and line breaks follow the incoming pressure. A practical walkthrough of wiring a TouchOSC-style device into a generative drawing patch.

## Mentions
- 0:28→1:07 [OscInput] · in-depth · scripted · Parameters · 90% — Teach mode is on by default: touch any control on the connected device and the OSC address auto-fills, so you rarely type addresses by hand; identical inputs on one port reuse a single shared connection.
- 1:19→1:49 [PickFloat] · explained · scripted · Example · 70% — Splitting a packed list of floats by index to recover separate X, Y and pressure channels from a single multi-value OSC message.
- 2:11→2:48 [Compare] · explained · scripted · Gotcha · 60% — Thresholding the pressure channel to decide where to break a stroke; note that a released touch may not return cleanly to zero, so test against a margin rather than exact zero.
- 2:48→2:56 [Remap] · passing · scripted · Example · 60% — Remapping the live pressure value into a color range that then drives the drawn line's color.
