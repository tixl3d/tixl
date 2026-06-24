---
video: WK4j0H-oM3g
type: update
date: 2023-06-18
title: Tooll V3.6 — release overview: color/gradient workflow, Symbol Browser, in-editor shaders, per-project playback, new ops
duration: 13:56
---

A scripted narrated walkthrough of the Tooll 3.6 release, covering color/gradient workflow improvements, keyframe and Symbol Browser enhancements, multi-connect, in-editor shader development, redesigned per-project playback/audio settings, and two new operators (RenderWithMotionBlur and DrawBillboards) plus the custom point shader. Most segments demonstrate features live while narrating, so depth ranges from passing mentions to explained how-tos.

## Mentions
- 0:28→0:59 [ui:ColorEditor] · explained · scripted · 88% — how the Color Picker's project swatches let you jump to and center every operator using a color, plus switching color input formats
- 0:59→1:14 [ImageLevels] · explained · scripted · 80% — using the image-level operator to read a project's color distribution and spot clamping when you push brightness
- 1:15→1:21 [ColorGrade] · passing · scripted · 55% — inserting color grading to push image brightness as a demo setup
- 1:26→1:53 [LinearGradient][ui:GradientEditor] · explained · scripted · 85% — new gradient interpolation modes (linear, smooth, spline) and when to reach for each
- 2:00→2:14 [RemapColor] · explained · scripted · 82% — Photoshop-style curve corrections on an image via the remap-color operator
- 2:23→3:07 [ui:DopeSheet] · explained · scripted · 85% — the new auto-pin animation mode that keeps all animated parameters visible while you switch operators, plus Shift+K to clear selection
- 3:07→3:36 [ui:SymbolBrowser] · in-depth · scripted · 90% — how Tab-inserting with an op selected uses relevancy stats to suggest ops to slot between existing ones
- 3:36→3:59 [ui:SymbolBrowser][AnimValue][WaveForm] · explained · scripted · 70% — dragging out a parameter to find animation ops, typing "AV" for Animated Value, and searching presets like a wave shape
- 3:59→4:46 [ui:SymbolBrowser][PickTexture] · in-depth · scripted · 80% — connecting many operators at once into a multi-input op, then pressing G to auto-layout the group
- 4:49→5:42 [ui:ShaderGraph] · explained · scripted · 70% — File>New image-effect template that scaffolds a shader, copies resources, and opens in your editor (VS Code + HLSL Tools recommended)
- 5:42→7:14 [ui:ParameterWindow] · in-depth · scripted · 65% — building a custom image effect: adding Vector2 params (Frequency/Amplitude), wiring them into the shader buffer, and a sine UV displacement
- 7:14→7:50 [ui:VariationWindow] · explained · scripted · 88% — using the Explore Variations window to randomize an effect's parameters and save discoveries as presets
- 7:50→8:46 [ui:OperatorSettings] · in-depth · scripted · 78% — new parameter settings: grouping/collapsing params, keeping modified ones visible, precision and suffix formatting
- 8:46→9:13 [ui:ProjectSettings] · explained · scripted · 80% — redesigned per-project playback/audio settings and enabling them for a new project
- 9:13→9:44 [ui:Timeline][LoadSoundtrack] · explained · scripted · 70% — soundtrack visualization in the timeline background, BPM/sync-offset, and bars/seconds/frames display for syncing
- 9:44→10:09 [ui:AudioInput] · explained · scripted · 78% — choosing audio input devices, reading the gain meter, and adjusting input gain for audio-reactive work
- 10:09→10:19 [AudioReaction] · explained · scripted · 88% — wiring an audio-reaction operator into a scene to drive audio-reactive effects
- 10:32→11:05 [RenderWithMotionBlur] · in-depth · scripted · 92% — the new render-with-motion-blur effect: multi-pass time-shifted rendering, great for animated particles
- 11:05→12:26 [CustomPointShader] · in-depth · scripted · 90% — the new custom point shader: inline compute-shader snippets per point via the f variable, fast enough for millions of points
- 12:26→13:01 [DrawBillboards] · in-depth · scripted · 90% — the new consolidated draw-billboards operator and using the W attribute for per-point color variation
- 13:01→13:43 [DrawBillboards][Text] · explained · scripted · 72% — feeding a texture as an atlas: building a procedural character atlas by slicing the viewport and looping over string characters
- 13:43→13:56 [Text] · passing · scripted · 50% — rendering the looped characters into a texture to form colorful character geometry
