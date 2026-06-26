---
video: _P4-VKhJXhE
type: tutorial
date: 2022-09-08
title: Tooll 3 Tip#019 - Color Grading
duration: 0:11:54
focusesOn: [ColorGrade], [ScreenCloseUp]
---

A five-minute introduction to color grading in Tooll, centered on the lift/gamma/gain controls of the ColorGrade operator, reading the WaveForm operator's scope, and comparing before/after with CompareImages.

## Mentions
- 0:27→0:38 [AdjustColors] [ColorGrade] · passing · scripted · Comparison · 80% — Reach past simple brightness/saturation tweaks toward a dedicated grading operator when you want full control over color and contrast in one place.
- 0:43→1:38 [WaveForm] · explained · scripted · Concept · 88% — Combines a vectorscope (hue/saturation distribution around a color wheel — reds top, blues lower-right, greens lower-left) with a brightness/luma plot across the image, so you can see where tones cluster and which channels are clipping before you touch a grade.
- 1:38→2:08 [ColorGrade] · explained · scripted · Concept · 85% — Lift/gamma/gain is the standard three-zone grading model: lift handles shadows, gamma the midtones, gain the highlights — powerful once you map each control to its tonal range.
- 2:08→2:54 [ColorGrade] · in-depth · scripted · Parameters · 88% — The lift wheels are pure 50% gray by default (no effect); right-drag raises shadows, but pushing below gray crushes them to black and introduces banding, so keep lift above neutral.
- 2:54→3:42 [ColorGrade] · in-depth · scripted · Gotcha · 88% — Cranking gain pushes highlights past pure white, where channels clamp and clip — visible as colors snapping onto the white point — so back off before the values flatten.
- 3:42→3:54 [ColorGrade] · explained · scripted · Parameters · 85% — Gamma reshapes the midtones while leaving shadows and highlights anchored, the safe control for overall brightness without clipping either end.
- 3:54→4:33 [ColorGrade] · in-depth · scripted · Tip · 85% — Tint each tonal zone toward complementary hues — warm highlights against teal shadows — to get the classic orange-and-teal cinematic look from the gain and lift color wheels.
- 4:33→4:55 [ColorGrade] · explained · scripted · Parameters · 80% — A pre-saturation control applied before the color correction; drop it toward zero to desaturate the source first, then push tonal tints onto the flattened image.
- 4:57→5:43 [ColorGrade] · explained · scripted · Parameters · 82% — A built-in vignette can be black or white, and its center is movable — slide it off-center to fake directional light spilling in from one edge rather than a symmetric darkening.
- 5:43→6:13 [ColorGrade] · explained · scripted · Parameters · 80% — In each color wheel the alpha of the swatch is the effect opacity: left-drag sets strength, but cranking alpha too far drives values out of bounds into clamping.
- 6:13→6:28 [ColorGrade] · passing · scripted · Tip · 78% — Hover a preset and hold Alt to blend it in gradually rather than applying it at full strength.
- 6:28→6:38 [TorusMesh] [DisplaceMesh] [DrawMesh] [PointLight] [Camera] · passing · scripted · Example · 70% — A quick 3D scene wired as a grading target: a displaced torus drawn with a colored point light and an orbiting camera.
- 6:38→6:55 [Glow] · passing · scripted · Example · 72% — Adding a glow pass before grading gives the highlights bloom that the grade then has to manage against clipping.
- 6:38→6:55 [ScreenCloseUp] · passing · scripted · Example · 55% — Wraps a flat render onto a virtual filmed LCD so the grade reads like real camera footage; pair it with [ColorGrade] and a glow pass to sell the photographed-screen illusion. Only briefly part of the demo scene here, not called out by name.
- 6:55→7:09 [RenderTarget] · passing · scripted · Tip · 72% — Tweaking the render target's background color shifts the overall tone of a scene before any grading is applied.
- 7:09→8:22 [WaveForm] [ColorGrade] · in-depth · scripted · Example · 85% — Use the scope as a clipping gauge while grading: when whites bunch against the top the highlights are clamped, so pull gain down until they spread out, then add midtone brightness back to rebalance.
- 8:22→9:01 [ui:ColorEditor] · explained · scripted · Concept · 78% — The grading color wheel uses a non-linear saturation radius — change is gentle near the center and steep at the rim — so subtle near-neutral tints are easy to dial in precisely.
- 9:01→11:04 [WaveForm] [ColorGrade] · in-depth · scripted · Example · 86% — Fixing a color cast: ignore the picture and watch only the vectorscope, dragging the highlight and shadow wheels toward the opposite hue until the blob recenters on neutral to restore white balance.
- 11:04→11:44 [CompareImages] · explained · scripted · Tip · 85% — Feed it two textures to A/B before and after a grade; rotate or split the divider to inspect exactly where clipping was fixed across the same region.
