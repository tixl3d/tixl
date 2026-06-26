---
video: 9qHY6pMnEcA
type: tutorial
date: 2025-08-25
title: Using Shader Graph and Particles in the Making of "Ashborn"
duration: 0:32:10
---

A scripted making-of walking through the TiXL "Ashborn" demo: keyframing every piano note, SDF-driven particle forces, a height-map landscape field, and a late refactor of tangled graph connections into context variables. Equal parts creative-process diary and operator tour of the shader-graph + particle setup.

## Mentions
- 1:44→1:56 [AudioReaction] · passing · scripted · Gotcha · 62% — Reacting to raw audio amplitude can be too imprecise and non-reproducible when you need frame-exact, repeatable timing — manual keyframes may beat it for tight sync.
- 2:04→2:23 [Modulo] [Blob] · explained · scripted · Example · 70% — A practical sync rig: animate a value, feed it through a wrapping operator, and drive a pulsing radius so you can tap keyframes in on the beat while audio plays.
- 2:41→2:47 [ui:DopeSheet] · passing · scripted · Gotcha · 60% — Inserting or removing keyframes forces a recompute of the curve values, and the editor surfaces glitches there.
- 3:06→3:35 [ui:CurveEditor] · explained · scripted · Gotcha · 68% — Hundreds of keyframes squashed to a parameter's value range become practically invisible; auto-scaling the curve to the visible keyframes makes a dense track readable again.
- 4:20→4:49 [TimeClip] · explained · scripted · Tip · 72% — Beyond switching between demo scenes, time clips double as labelled annotations to carve a soundtrack into named sections you can navigate.
- 5:59→6:31 [TurbulenceForce] · explained · scripted · Example · 70% — Record per-note audio volume into a keyframe track and pipe that envelope into a turbulence force so particle motion accelerates on louder notes.
- 7:08→7:34 [Sketch] · passing · scripted · Tip · 60% — Switching medium to rough on-canvas sketching (with onion-skin) to block out a storyboard before committing to particle work.
- 8:23→8:33 [GridPlane] · passing · scripted · Tip · 58% — A quick background reference plane handy for rough blocking and judging proportions of an effect.
- 9:18→9:33 [RandomJumpForce] · passing · scripted · Example · 58% — Makes particle ribbons jump abruptly on note triggers — a striking look, though it can clash with a calmer mood.
- 9:52→10:33 [Grain] [Time] · explained · scripted · Performance · 74% — A built-in animation-speed knob avoids wiring a time source, but a "frozen" time mode is what lets a static result stop invalidating and actually cache instead of recomputing every frame.
- 10:41→11:04 [ParticleSystem] · explained · scripted · Parameters · 65% — An emit-velocity control lets you randomize and scatter points slightly as they spawn, loosening an over-uniform emission.
- 11:28→12:16 [ToroidalVortexField] · explained · scripted · Example · 72% — Twists particles into a torus/mushroom-cloud shape — faking such a swirl by hand turned out harder than driving it with this purpose-built field.
- 12:16→12:45 [ui:Field] · explained · scripted · Concept · 66% — Fields can return not just distances and colors but vectors, and vector fields compose under multiply/transform like any other field.
- 12:57→13:55 [DrawMesh] [RaymarchField] · explained · scripted · Gotcha · 70% — A view-direction bug in the PBR specular term made mesh and ray-marched-field surfaces shade differently; unifying the PBR shading into a shared include keeps both paths visually aligned.
- 14:54→15:17 [ui:EvaluationContext] · in-depth · scripted · Concept · 76% — Refactoring a graph criss-crossed with long connections into per-frame context variables (booleans, ints, vectors) set once up front and read wherever needed downstream.
- 15:44→16:16 [HeightMapSdf] · explained · scripted · Example · 72% — Turns a single noise channel into terrain as a distance-field operator, pairing cleanly with an SDF-to-color step for a procedural landscape.
- 16:16→16:37 [FractalNoise] · explained · scripted · Parameters · 72% — Drive a height field from one color channel; gain and bias are the knobs to reach for when shaping the silhouette of the mountains.
- 16:37→16:49 [SinForm] · passing · scripted · Example · 58% — Used to carve a valley through a noise-based mountain range.
- 16:53→17:16 [SetSDFMaterial] [SDFToColor] · explained · scripted · Example · 64% — Read the plain distance from ground level, perturb it with noise, then map that distance onto a color gradient to paint a terrain SDF.
- 17:32→17:46 [FieldVolumeForce] · explained · scripted · Example · 74% — Attract particles to a distance-field surface and transfer the field's color to each particle as it collides with the surface.
- 21:47→21:58 [SSAO] [Bloom] [Glow] · passing · scripted · Example · 62% — A restrained post chain — screen-space occlusion plus bloom and glow, no color grade — is enough to finish a procedural scene.
- 22:02→22:11 [PerlinNoise] · passing · scripted · Example · 62% — A small noise source on a camera's rotation parameter gives a subtle handheld camera-shake.
- 22:33→22:51 [DrawCamGizmos] · explained · scripted · Tip · 68% — Shows a camera's frustum within the scene, but only once you switch the output window's camera mode from auto to viewer.
- 22:52→23:05 [VisibleGizmos] · explained · scripted · Tip · 64% — Toggles whole sets of helper elements on and off via the gizmo grid icon in the output window — handy for hiding scaffolding during playback.
- 24:13→24:29 [SetFog] · passing · scripted · Gotcha · 56% — Fog is fiddly to tune when its tint animates over the course of a scene.
- 24:29→24:42 [GridPoints] · passing · scripted · Tip · 60% — Randomizing position and scale (and offsetting the random phase) keeps a scattered point field from overlapping a neighbouring element.
- 24:42→24:59 [RadialPoints] · explained · scripted · Example · 64% — A ring of emit points, color-mapped through a gradient, as the seed for ribbon particles.
- 24:59→25:46 [VelocityForce] · explained · scripted · Example · 68% — In a stack of guiding forces, a velocity push triggered on each note nudges particles forward for a beat-responsive surge.
- 25:46→25:52 [PointTrail] · passing · scripted · Example · 62% — Pipe a particle system into a trail to record and draw the ribbon each point leaves behind.
- 25:52→26:36 [SpherePoints] · explained · scripted · Example · 66% — Replicate a small sphere of points on each emitter point, gradient-color them, and jitter their radial distance to build a soft scattered burst.
- 28:41→28:47 [PushPullSDF] · passing · scripted · Tip · 58% — Smooths out and reduces apparent complexity of a busy distance field.
- 28:47→29:00 [VisualizeFieldDistance] · explained · scripted · Tip · 66% — Renders the shape of a distance field so you can see how it's structured when particles won't follow it the way you expect.
- 29:07→29:20 [MoveToSDF] · explained · scripted · Example · 66% — Pull a set of points onto a distance-field surface to preview how the field will drive them before committing.
- 29:24→30:00 [ReconstructiveForce] · explained · scripted · Example · 70% — Drives particles toward an arbitrary target point set — e.g. replicated grid points randomized into an abstract structure — to reform them into a shape.
- 30:00→30:06 [ui:GradientEditor] · passing · scripted · Tip · 56% — Procedural gradients and color lists let you randomly sample a constant gradient to tint a generated point set.
- 30:32→31:09 [PointSimulation] · explained · scripted · Example · 64% — Stamp/bake the carefully animated motion of a small control-point set so a downstream reconstructive force can guide the main ribbons precisely through the scene.

UNSURE: SinForm ("sign form" — likely the operator that carves the valley); SetSDFMaterial ("ZSDF material" — ASR-garbled, the SDF-coloring material operator); Glow ("growing" in the post chain — heard as a mishear of Glow).
