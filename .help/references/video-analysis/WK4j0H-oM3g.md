---
video: WK4j0H-oM3g
type: update
date: 2023-06-18
title: Tooll V3.6 / Overview and New Features
duration: 0:14:22
---

Release walkthrough of Tooll 3.6: color and gradient workflow improvements, dope-sheet auto-pin, smarter symbol-browser relevancy and multi-connection, the image-effect shader template, per-project playback/audio settings, and two new operators (RenderWithMotionBlur and DrawBillboards) plus the custom point shader.

## Mentions
- 0:28→0:59 [ui:ColorEditor] · explained · scripted · Tip · 80% — Swatches surface every color used in the project; Ctrl-clicking a swatch selects and centers all operators that use it, so you can audit and unify a palette across a graph.
- 0:49→0:59 [ui:ColorEditor] · passing · scripted · Parameters · 70% — Switch the input format (HSB, RGB, etc.) by clicking the type label rather than converting values by hand.
- 1:04→1:25 [ImageLevels] · explained · scripted · Concept · 80% — Drop it on an image to read the live color distribution of a buffer slice; pushing brightness up makes it flag where channels clamp, useful for spotting blown-out grades.
- 1:18→1:25 [ColorGrade] · passing · scripted · Example · 65% — Inserted upstream to raise brightness and demonstrate clamping; a quick way to test how far you can push exposure before detail is lost.
- 1:26→1:46 [LinearGradient] · explained · scripted · Parameters · 80% — Beyond the default linear ramp, switch its interpolation to smooth for visibly softer transitions between stops.
- 1:46→2:00 [LinearGradient] · explained · scripted · Tip · 78% — A spline interpolation mode lets you hand-tune the ramp shape between stops for very fine gradient control.
- 2:00→2:13 [RemapColor] · explained · scripted · Example · 78% — Pair it with a spline gradient to apply Photoshop-style curve corrections to an image, from subtle grades to extreme remaps.
- 2:23→3:07 [ui:DopeSheet] · explained · scripted · Tip · 82% — By default it only shows keyframes of selected operators; the new auto-pin mode keeps every animated parameter visible while you jump between ops, and Shift+K clears the pinned set to start a fresh animation group.
- 3:07→3:30 [ui:SymbolBrowser] · explained · scripted · Concept · 80% — Tab over a single selected operator inserts the new op inline into the connection; suggestions are now ranked by statistical usage, so common follow-ups (e.g. what people typically place between two ops) float to the top.
- 3:30→3:59 [ui:SymbolBrowser] · explained · scripted · Tip · 75% — Dragging out from a parameter offers ops that drive that value; pressing Space after picking an animation op lets you search its presets (e.g. a wave shape to modulate a value).
- 3:36→3:59 [AnimValue] · passing · scripted · Example · 60% — Typed as "AV" to wire an animated value onto a parameter, then a wave-shape preset chosen to drive it.
- 3:59→4:49 [ui:SymbolBrowser] · explained · scripted · Tip · 80% — Select several operators, drag out one connection group, and the browser lists every op with a matching multi-input so you can fan many sources into one; press G afterward to auto-layout the new group.
- 4:41→4:49 [PickTexture] · passing · scripted · Example · 62% — Used as the multi-input target when fanning several image operators into a single combined input.
- 4:49→5:42 [ui:ShaderGraph] · explained · scripted · Concept · 78% — File > New scaffolds an image effect from a template, copying needed resources into your namespace and opening the shader in your editor; VS Code with the HLSL Tools extension is recommended for highlighting, autocomplete, and error squiggles.
- 5:42→6:27 [ui:ShaderNode] · explained · scripted · Parameters · 75% — The generated effect ships as a detect-edges template with configurable settings (e.g. texture mode set to Wrap); its context menu adds typed input parameters such as a Vector2 split into floats wired to the shader's parameter buffer.
- 6:27→7:14 [CustomPixelShader] · explained · scripted · Example · 70% — After declaring frequency/amplitude params in the buffer at the top of the file, add a sine offset to the UV coordinates to get a live displacement; nothing moves until amplitude is raised above zero.
- 7:14→7:45 [ui:VariationWindow] · explained · scripted · Tip · 80% — Select an effect, choose which of its parameters to vary, and generate random variations with adjustable randomization strength; any result you like is saveable as a preset.
- 7:45→8:40 [ui:ParameterWindow] · explained · scripted · Parameters · 80% — Per-parameter settings let you group knobs (append "..." to a group name to collapse it by default, while modified params still show), raise display precision, and add a unit suffix that's handy for marking rotations.
- 8:40→9:54 [ui:ProjectSettings] · in-depth · scripted · Concept · 82% — Redesigned per-project playback/audio: enable playback settings, pick soundtrack vs. live-performance mode, set BPM and a sync offset for tracks that don't start on the downbeat, and choose bars/seconds/frames for the timeline ruler; live mode adds tap-tempo VJ playback and external MIDI-clock sync.
- 9:13→9:33 [ui:Timeline] · passing · scripted · Tip · 70% — With a soundtrack assigned, the timeline background renders the audio waveform so you can align keyframes to the music.
- 9:54→10:20 [AudioReaction] · explained · scripted · Example · 78% — Pick an audio input device, set its gain via the level meter, then create this op and connect it to a scene to drive audio-reactive effects; the chosen device/gain is stored per project.
- 10:20→11:05 [RenderWithMotionBlur] · in-depth · scripted · Concept · 85% — Renders a scene over several passes with the time nudged slightly each pass to accumulate true motion blur, which works especially well on fast-moving animated particles; feed it a camera and MSAA-rendered scene.
- 10:33→10:44 [FractalNoise] · passing · scripted · Example · 55% — Noise added to a point cloud and its phase animated to create the moving particles used to demonstrate motion blur.
- 11:05→12:21 [CustomPointShader] · in-depth · scripted · Example · 82% — Insert it after drawing points to run an on-the-fly compiled GPU snippet per point; each point exposes an f variable running 0→1 across the set, and writing to position gives sine displacement while writing to the w attribute yields a wave-shaped field—fast enough for millions of points.
- 11:28→11:35 [CustomPixelShader] · passing · scripted · Comparison · 60% — The custom point shader uses string-replacement to splice your snippet, equivalent to authoring a full compute-shader operator from the menu template but far quicker to experiment with.
- 12:21→12:34 [MeshVerticesToPoints] · passing · scripted · Example · 55% — Swap a generated point set for a loaded mesh's vertices to drive a per-point shader over real geometry.
- 12:34→13:03 [DrawBillboards] · in-depth · scripted · Concept · 82% — Consolidates the many older point-drawing methods into one flexible operator; its color-variations option can map a point's w attribute to per-point color.
- 13:03→13:43 [DrawBillboards] · explained · scripted · Tip · 78% — Feed it a texture and enable texture-atlas mode to give each point a different sprite cell; build the atlas procedurally by slicing the viewport into a grid and using the loop index to place a character per cell.
- 13:18→13:43 [TextSprites] · passing · scripted · Example · 55% — Characters of a string rendered into viewport cells to assemble a procedural atlas texture for billboard sprites.
