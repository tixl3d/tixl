---
video: 6-8PKxPztl8
type: tutorial
date: 2025-10-13
title: Using the new Shader Graph (Deep Dive)
duration: 0:20:13
focusesOn: [ui:ShaderGraph], [RaymarchField]
---

A guided tour of TiXL's new SDF/field shader-graph system: the four operator categories (generators, space, adjust, combine), how ray-marching renders fields, performance and aliasing trade-offs, and using fields to drive particle forces and point selection.

## Mentions
- 0:08→0:45 [ui:ShaderGraph] · explained · scripted · Concept · 90% — Combines small per-operator shader building blocks into one shader function compiled on the fly, replacing the old fixed one-shader-per-operator pipeline.
- 1:42→2:30 [RaymarchField] · explained · scripted · Concept · 88% — Renders a volume by stepping a distance function instead of polygons, so there are no UV coordinates and texturing/PBR material support is limited.
- 2:14→2:20 [SetFog] [SetMaterial] · passing · scripted · Example · 80% — Both still drive the look of a ray-marched field exactly as they do for mesh rendering, keeping the two pipelines aligned.
- 2:58→3:14 [AddNoise] · explained · scripted · Example · 78% — Cranking the noise offset on a field breaks a surface into isolated floating blobs — a shape impossible with traditional mesh displacement.
- 3:36→3:48 [PushPullSDF] · explained · scripted · Example · 82% — Inserting it offsets a field's boundary layer, growing or shrinking the surface to reveal particles underneath.
- 3:48→4:08 [RepeatPolar] · explained · scripted · Gotcha · 82% — Replicates space around the local Y axis on the Z axis, so an object centred at the origin vanishes — offset it along Z to bring it back into the repeated cell. In ray-marching the repetition is free regardless of count.
- 4:39→5:09 [ui:Field] · explained · scripted · Concept · 85% — The four shader-graph categories: generators emit a distance or color per position, space ops fold/replicate space, adjust ops modify the returned value, and combine ops merge or blend fields.
- 5:39→6:00 [ui:ShaderGraph] · explained · scripted · Performance · 78% — Some generator knobs are "shader variation" parameters: changing them forces a graph recompile, so they can't be animated or driven by another operator.
- 5:55→6:03 [ui:ProjectSettings] · passing · scripted · Tip · 72% — Disabling shader optimization here makes recompiles after parameter changes noticeably faster while authoring.
- 6:35→7:05 [ui:Field] · explained · scripted · Concept · 80% — Generators return four values, not one: a signed distance plus the object-space coordinates needed for texturing, and fields can additionally return an RGB color for procedurally coloring meshes.
- 9:09→9:53 [RepeatPolar] · explained · scripted · Gotcha · 80% — Objects straddling a fold boundary tear; enabling the mirror option hides the seam, and nesting several repeats builds complex geometry at no extra render cost.
- 10:00→11:08 [TwistField] [BendField] · explained · scripted · Gotcha · 80% — Unlike clean space-folding, twisting and bending warp the distance field so ray-marching mis-estimates step lengths and produces artifacts as contour lines bunch up.
- 10:43→11:08 [VisualizeFieldDistance] · explained · scripted · Tip · 82% — Draws a field's contour lines so you can see where the distance estimate degrades; evenly spaced lines mean a well-behaved field, squished ones predict ray-march artifacts.
- 11:08→11:57 [AddNoise] · explained · scripted · Gotcha · 80% — Adding 3D noise to a distance looks great animated, but large offsets break the field's distance estimate and force a smaller ray-march step to render cleanly.
- 11:57→12:10 [RaymarchField] · explained · scripted · Performance · 82% — Lowering the step-size factor recovers detail in a distorted field, at the cost of more steps and slower rendering.
- 12:10→12:50 [CombineSDF] · in-depth · scripted · Parameters · 85% — Ctrl+mouse-wheel cycles the union/intersect/blend modes live; the K parameter sets blend smoothness, e.g. intersecting a fractal with a box to expose its interior.
- 12:37→12:50 [StairCombineSDF] · explained · scripted · Parameters · 78% — A stepped variant of the smooth combine whose extra parameter sets the number of grooves/stairs in the blend seam.
- 12:50→15:15 [RaymarchField] · in-depth · scripted · Performance · 85% — The algorithm marches each view ray forward by the field's safe distance until within the min-distance threshold or the max step count; smaller min-distance sharpens edges but needs more steps, and the normal-sampling distance trades crisp versus organic shading.
- 15:09→16:22 [CustomSDF] · explained · scripted · Tip · 80% — A live shader-code playground for distance functions; GLSL fractals port to HLSL easily, and swapping magic numbers for the A/B/C/D offset variables makes them animatable, preset-able, and blendable.
- 16:30→17:43 [FieldVolumeForce] · in-depth · scripted · Example · 85% — Feed it a field (e.g. a torus) to attract or repel particles toward that surface; transform the field via [TransformField] driven by [PerlinNoise3] for turbulence, and combine with a directional force to trap particles inside an inverted volume.
- 16:38→16:46 [LinePoints] [ParticleSystem] · passing · scripted · Example · 72% — Emitting line points into a particle system and drawing them as points is the quick starting setup before adding a field force.
- 17:16→17:43 [TransformField] · explained · scripted · Example · 80% — Wrapping a field in it lets you rotate/offset the whole space; wiring its rotation to noise animates the force region without per-step cost.
- 17:43→19:02 [SelectPointsWithSDF] · in-depth · scripted · Example · 84% — Uses an SDF to write each point's fx1/fx2 attribute, which you then feed as the strength factor of another effect (e.g. an [AddNoise] displacement) to mask it by region; the mapping parameter can repeat or animate the selection.
- 19:02→19:12 [MoveToSDF] · explained · scripted · Example · 80% — Snaps a point cloud onto a target SDF surface, e.g. pulling a grid of points onto a torus.
