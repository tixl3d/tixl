---
video: MAyETISMFi0
type: meetup
date: 2026-02-03
title: "TiXL Meetup 2026-01-26 / Part 2: SkillQuest"
duration: 0:34:37
---

Improvised newcomer intro on a meet-up stream: the host walks a graphic designer through building a procedural logo animation from a loaded image — image effects, gradient sampling, oscillator-driven parameters, key/feedback, directional blur, and gradient masks — while wrestling with streaming-induced slowdowns.

## Mentions
- 5:55→7:05 [FakeLight] · explained · discussion · Concept · 70% — Cheap fake relighting of a 2D image stands in for full PBR; the host's wishlist (normal map + environment texture + bloom) marks the line between this quick effect and a real HDR relight.
- 11:23→12:00 [ui:Graph] · passing · discussion · Concept · 80% — The whole composition is one node graph you freely arrange operators on, then dive into sub-graphs; framed for someone coming from layer-based tools.
- 12:09→12:27 [ui:OutputSettings] · passing · discussion · Concept · 65% — The final image is rendered at a resolution you set (or override) at the top, much like a game-engine frame, before any operators are stacked on.
- 12:42→13:09 [LoadImage] · explained · discussion · Example · 80% — Unpinning its output shows the image directly; it's the typical starting node you then branch off in two directions to add effects.
- 13:18→13:36 [ColorGrade] · passing · discussion · Tip · 70% — Hold Alt while hovering its thumbnail to flip through built-in presets instead of dialing every knob by hand.
- 13:43→13:55 [ui:ParameterWindow] · passing · discussion · Concept · 70% — The selected operator's knobs live in a side panel; the per-parameter keyframes show up alongside them.
- 14:14→14:35 [ui:IdleMotion] · explained · discussion · Concept · 75% — Even with no timeline running, the scene animates continuously at a steady rate — the always-moving "idle" playback that lets procedural setups breathe without keyframes.
- 15:22→15:43 [ui:SearchWindow] · explained · discussion · Concept · 75% — Clicking a parameter's input opens a typed search box to drop a driving operator straight onto that input; Alt-clicking instead adds a keyframe.
- 15:43→16:08 [SampleGradient] · explained · discussion · Example · 85% — Reads a color out of a gradient at a given position, so you define start/end colors once and then drive the read-out position to sweep through them.
- 16:08→16:54 [AnimValue] · in-depth · discussion · Example · 85% — Drop it onto a parameter for keyframe-free motion: it oscillates at a rate (e.g. 120 BPM) and its shape can switch from a pulse to a back-and-forth ramp.
- 16:54→17:39 [PerlinNoise] · explained · discussion · Example · 80% — Add it on top of a smooth oscillator to layer in organic jitter that still roughly follows the base curve; raise its frequency to make the wobble faster and rougher.
- 18:29→19:30 [KeyColor] · explained · experiment · Example · 75% — Pick a target color (e.g. a skin tone) to key it out; turn up the amount and switch its output to return just the mask rather than the keyed image.
- 20:33→20:40 [BlendWithMask] · passing · discussion · Tip · 70% — A favorite for compositing two images through a mask channel, useful for layering a keyed result over a background.
- 20:40→21:54 [DirectionalBlur] · in-depth · discussion · Concept · 80% — Smears the image along an angle like Photoshop's motion blur, but its angle/strength can be driven per-pixel by a second texture, so feeding the image back into itself warps the smear into swirling distortion.
- 21:54→22:13 [FastBlur] · passing · discussion · Comparison · 65% — Swapped in for a cheaper, more striking blur when the plain blur looks flat.
- 22:45→22:50 [ui:Timeline] · passing · discussion · Tip · 60% — At any point you can drop keyframes to pin a parameter and scrub a deterministic animation, unlike non-deterministic feedback which won't reproduce the same frame.
- 23:08→23:40 [DirectionalBlur] · explained · experiment · Parameters · 70% — A refinement-pass count smooths the result, and the blur angle is the knob you animate to morph between the warped look and the untouched original.
- 24:53→25:09 [KeyColor] · passing · experiment · Tip · 55% — Re-keying and nudging exposure up cleans the mask before blending it onward.
- 25:09→25:55 [BlendWithMask] · explained · experiment · Example · 70% — To layer a logo over the effect, wire the background to the first input and the logo to the second; an already-connected input is what blocks a re-plug.
- 25:48→26:05 [LinearGradient] · explained · experiment · Example · 70% — Generate a band with an offset and width you animate, then use it as the wipe mask that reveals a logo over time.
- 26:05→26:22 [TransformImage] · passing · experiment · Tip · 65% — Scale and rotate an image (here the gradient mask) in 2D before it feeds the next stage.
- 29:47→30:35 [RadialGradient] · explained · experiment · Example · 75% — A dark-to-transparent radial, flipped so the center is clear, makes a vignette-style mask whose animated offset gently reveals the framed subject.
- 33:07→33:24 [WaveForm] · passing · discussion · Tip · 55% — A live waveform readout helps judge where the image is getting too dark before pulling mid-tones up with color grading.
- 33:15→33:24 [ColorGrade] · passing · discussion · Tip · 60% — Lift the mid-tones to add contrast and rescue a too-dark composite.
