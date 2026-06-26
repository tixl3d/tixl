---
video: M7eOmqiUOZM
type: tutorial
date: 2022-09-11
title: Tooll 3 Tip#22 - Lost in Feedback
duration: 0:16:59
focusesOn: [AdvancedFeedback], [AdvancedFeedback2]
---

A deep walkthrough of the AdvancedFeedback effect: how its displace-along-a-slope feedback loop works internally, and a guided tour of nearly every parameter (twist, displacement, sample distance, shade, blur, color shifts, amplified edges, limit brights) and how to drive them for smoke-, water- and reaction-diffusion-like looks.

## Mentions
- 1:18→2:32 [AdvancedFeedback2] · explained · scripted · Concept · 70% — The same slope-displacement feedback loop walked through here is the basis the v2 revision builds on; the displace-along-a-gradient core, color clamping and per-frame layer transform all carry over, so this tour explains how to drive it even though the newer revision itself isn't shown.
- 0:21→0:57 [AdvancedFeedback] · explained · scripted · Example · 90% — Holding Alt while scrubbing its preset thumbnails blends between looks, showing the same effect can yield reaction-diffusion, water, oily-color and twirl results from one text input.
- 1:18→2:09 [AdvancedFeedback] · in-depth · scripted · Concept · 88% — The internals: a feedback loop reads the previous output, clamps color to a valid range so the sim doesn't blow up to NaN, runs edge detection, blurs the image, then uses the blurred copy as a slope to displace itself.
- 1:33→1:56 [RenderTarget] · explained · scripted · Concept · 70% — Used as the feedback buffer: re-reading its last rendered frame each step is what closes a self-referential simulation loop. (Spoken as "use render target".)
- 2:09→2:32 [Layer2d] · explained · scripted · Example · 78% — Rendering the feedback into a 2D layer lets you offset, rotate and zoom it per frame — that transform is what turns the loop into rising smoke, falling water or classic video-feedback spirals.
- 2:32→2:53 [AdvancedFeedback] · explained · scripted · Tip · 82% — Pausing freezes the buffer in place; rewinding the timeline resets it — handy for clearing or holding the accumulated simulation state.
- 2:59→4:46 [ImageLevels] · in-depth · scripted · Concept · 85% — Sampling brightness along a line across the image reveals it as a height field; bright letters read as a slope, and the effect flows the image downhill along that slope — the core trick behind the whole feedback.
- 4:46→5:56 [AdvancedFeedback] · in-depth · scripted · Parameters · 90% — Twist (in degrees) rotates the displacement relative to the slope: 0° flows straight downhill away from bright areas, 90° circles around them like contour lines, and beyond that it flows inward toward bright areas so they consume themselves into churning patterns.
- 5:56→7:19 [AdvancedFeedback] · in-depth · scripted · Parameters · 88% — Displacement is the flow speed (zero freezes the image); a separate displace-offset pushes a constant direction regardless of slope steepness, and the two forces fighting each other produce the most organic, oily reaction-diffusion movement.
- 7:19→7:59 [AdvancedFeedback] · explained · scripted · Parameters · 85% — Sample distance sets how many pixels apart the slope/edge is measured: larger makes the effect bigger but coarser, adding noise and artifacts you may or may not want.
- 7:59→8:51 [AdvancedFeedback] · explained · scripted · Parameters · 84% — Shade fakes a 3D bevel by brightening along the gradient; that added brightness itself feeds back into the sim, and pushing it negative darkens until the reaction explodes to pure white.
- 8:51→9:50 [Blur] · explained · scripted · Performance · 78% — The internal blur (~40 samples) is the effect's main cost; raising its radius can be pleasing but its fixed sample count makes results resolution-dependent — the same settings look different at 4K.
- 10:02→10:16 [AdvancedFeedback] · passing · scripted · Parameters · 65% — Twist is revisited as twisting the displacement along the gradient direction.
- 10:16→10:50 [Layer2d] · explained · scripted · Tip · 75% — Zooming the layer per frame is the easy "growing tendrils" trick; a tiny zoom reads as intriguing organic growth while a strong rotate/zoom looks cheesy and generic.
- 10:50→11:42 [AdvancedFeedback] · explained · scripted · Parameters · 80% — Horizontal/vertical offset combined with an inward twist and reduced displacement biases the flow upward to read as drifting smoke.
- 11:42→12:48 [AdvancedFeedback] · in-depth · scripted · Parameters · 84% — Color shifts (shift-U/hue, shift-saturation, shift-brightness) apply across the whole image each step; with a colored text input the hue creeps over time into rainbows, and the per-channel values subtly perturb the displacement too.
- 12:50→13:57 [AdvancedFeedback] · in-depth · scripted · Parameters · 85% — Amplified-edges adds edge detection at fringes (a little is on by default); cranking it makes the image unstable and eventually explodes, but countering with a slightly darker brightness shift gives self-sustaining pumping edge patterns.
- 13:57→15:01 [ImageLevels] · explained · scripted · Tip · 70% — Compositing the original readable image back on top of the feedback result is a simple way to keep text legible while using the effect as a post-process background.
- 15:08→16:05 [AdvancedFeedback] · explained · scripted · Parameters · 82% — Limit-brights (zero = no limiting) caps runaway brightness so an over-bright simulation stays within a usable range instead of washing out.
- 16:05→16:12 [Blur] · passing · scripted · Parameters · 60% — The blur sample radius is noted again as another knob worth playing with for varied looks.
- 16:12→16:41 [ui:AudioInput] · passing · scripted · Tip · 60% — Mapping the effect's parameters to MIDI controllers or audio reaction makes it a standalone live-performable visual on its own.
