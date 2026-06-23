---
video: MAyETISMFi0
type: meetup
date: 2026-01-26
title: Part 2 (SkillQuest) — Performance debugging, procedural animation, gradient sampling, directional-blur feedback, masked logo reveal
duration: 34:08
---

A casual TiXL community meet-up (Part 2, SkillQuest) where the host debugs stage-recording slowdown while improvising a graph, then gives a newcomer a guided tour of node-based workflow. The walkthrough covers loading an image, color grading, sampling gradients to drive animated values without keyframes, building shaky procedural motion, and a directional-blur "twist" feedback effect culminating in a masked, animated logo reveal.

## Mentions
- 2:13 [Blend] · passing — routes the current element into a blend while experimenting with the live graph
- 2:42 [ParticleSystem] · passing — asks how many particles exist while hunting for the slowdown; confirms it's not particles
- 6:23 [FakeLight] · passing — notes its potential; muses about a future PBR/HDR-shaded fake-light tool
- 6:51 [Bloom] · passing — mentions adding bloom on top of the fake-light rendering idea
- 12:44 [LoadImage] · explained — loads an image and unpins its output to display it; the starting point of the tour
- 13:15 [Mirror] · passing — suggested as an easy follow-on op (e.g. mirror the image)
- 13:20 [ColorGrade] · explained — demonstrated for color grading with subnet previews and Alt-to-pick presets
- 15:36 [SampleGradient] · in-depth — adds a gradient with start/add colors; sampling a gradient at a position to drive a value
- 16:08 [AnimValue] · explained — sample position turned into an animated value (FPS/BPM-rate idle motion, no keyframes)
- 16:33 [TriggerAnim] · explained — changing the curve shape (e.g. back-and-forth) for a keyframe-like animation without keyframes
- 17:00 [Noise] · explained — noise/jitter on top of the value so the animation roughly follows the curve but shakes
- 18:27 [Feedback] · passing — suggests converting the image into feedback; notes a whole family of feedback ops exists
- 18:40 [PickColor] · explained — tries to pick a skin-tone color from the image (color picker misbehaves)
- 19:26 [KeyColor] · explained — keys out a color, adjusts amount, returns just the mask/key
- 20:36 [BlurWithMask] · passing — named as a liked post-processing op
- 20:43 [DirectionalBlur] · in-depth — core effect: directional blur driven by an X-factor texture to twist/stretch; "MRI"-like results
- 21:54 [FastBlur] · passing — mentioned as an alternative producing more twisting than the last blur
- 22:48 [PixelLogo] · explained — turning the result into a pixel/point logo for a quick deterministic animation
- 23:08 [RefinementPass] · explained — cranked up for a smoother image; rotates the angle to reveal/morph back to the original
- 24:47 [Exposure] · passing — increases exposure on the keyed result
- 24:56 [Amplify] · passing — used to amplify the result before blending
- 25:10 [Blend] · explained — blends the twisted result with the loaded logo image (first/second order matters)
- 25:21 [BlendWithMask] · explained — switches to masked blending to combine logo and effect
- 25:49 [LinearGradient] · explained — adds a linear gradient; animates its offset to mask/reveal the logo
- 25:57 [LayerMove] · passing — a "less move"/offset move op for positioning the gradient/mask
- 26:05 [TransformImage] · explained — transforms/scales the image smaller and positions it
- 29:39 [RadialGradient] · explained — radial gradient as a soft vignette-style mask, dark-to-transparent, with an animated offset reveal
- 33:09 [Levels] · explained — curves/levels to lift mid-tones and add contrast on the too-dark image
