---
video: 6-8PKxPztl8
type: tutorial
date: 2025-10-13
title: Using the new Shader Graph (Deep Dive) — SDFs, ray marching, and field-driven particles
duration: 20:13
---

A scripted single-narrator walkthrough of TiXL's new shader-graph (SDF / field) system: how pink field operators replace stacks of fixed shaders, the four operator categories (generators, space, adjust, combine), and how ray marching renders volumes without polygons. The second half shows fields driving particle forces and point selection, plus the live-coding CustomSDF playground for fractals.

## Mentions
- 0:18→1:21 [ui:ShaderGraph] · explained · scripted · 95% — why the shader graph replaces one-shader-per-operator with on-the-fly compiled field functions, and the duplication it removes
- 1:21→1:42 [ui:ShaderGraph][ui:Field][SphereSDF] · explained · scripted · 80% — collapsing three nodes (volume force, point selection, mesh sphere) into a single sphere field
- 1:42→2:30 [RaymarchField][ui:Field][SetFog][SetMaterial] · explained · scripted · 88% — first look at ray marching a volume, and using [SetFog]/[SetMaterial] to control its look (no UVs, partial PBR)
- 2:30→2:55 [ui:ShaderGraph] · passing · scripted · 70% — how the new field ops are aligned with classic mesh-rendering ops rather than replacing them
- 2:55→3:14 [AddNoise][RaymarchField] · explained · scripted · 75% — cranking the noise offset to spawn isolated SDF blobs impossible with mesh displacement
- 3:14→3:44 [PushPullSDF][ui:Field] · explained · scripted · 80% — aligning the three field effects and inserting a push-pull field to offset the boundary layer; material opacity for particle visibility
- 3:44→4:18 [RepeatPolar] · explained · scripted · 82% — adding a polar repeat and why space repetition is essentially free with ray marching
- 4:25→4:45 [ui:SymbolLibrary][ui:ShaderGraph] · passing · scripted · 80% — finding all the pink shader-graph ops and the four operator categories overview
- 4:45→5:17 [ui:Field] · explained · scripted · 78% — the four field categories: generators, space, adjust, and combine operators
- 5:17→6:03 [SphereSDF][BoxSDF][TorusSDF][ui:Field] · explained · scripted · 80% — SDF generators as fast math functions returning signed distance; shader-variation params that force recompile and the project-settings optimization toggle
- 6:03→6:13 [ui:ProjectSettings] · passing · scripted · 70% — disabling shader optimization in project settings to speed up recompiles
- 6:13→7:13 [CustomSDF][ui:Field] · explained · scripted · 72% — why generators return four values (object-space coords for texturing plus optional RGB to colorize meshes), and the unified field/SDF concept
- 7:13→9:09 [RepeatPolar][ui:Field] · in-depth · scripted · 78% — the mental model of space manipulation: fields shift space first, the "rooms/teleport" analogy, and why objects crossing boundaries cause artifacts
- 9:09→9:53 [RepeatPolar] · explained · scripted · 80% — the polar-repeat mirror option to fix artifacts, and nesting repeats to build complex geometry for free
- 9:53→10:43 [BendField][TwistField][ui:Field] · explained · scripted · 75% — bend/twist warp space (vs folding) and why they break ray marching; Lipschitz continuity and even contour spacing
- 10:43→11:08 [VisualizeFieldDistance] · explained · scripted · 85% — using the field-distance visualizer to inspect contour lines and spot distortion artifacts
- 11:08→11:26 [RaymarchField] · explained · scripted · 78% — reducing the step-size factor to tame noise/twist artifacts and its performance cost
- 11:26→12:00 [PushPullSDF][AddNoise] · explained · scripted · 82% — adjust operators: push-pull shrinks/grows an SDF, add-noise adds animated 3D noise to the distance
- 12:00→12:37 [CombineSDF] · in-depth · scripted · 85% — combine operators: ctrl+mousewheel through union/intersect/blend methods and the K smoothness parameter
- 12:37→12:54 [StairCombineSDF] · explained · scripted · 85% — the stair-combine grooves/stairs parameter for retro-architecture structures
- 12:54→14:45 [RaymarchField] · in-depth · scripted · 88% — the ray-marching algorithm itself (~200M calls/frame), min-distance and max-step-count parameters, and psychedelic under-stepping
- 14:45→15:09 [RaymarchField] · explained · scripted · 75% — computing surface normals via the normal-sampling parameter for sharp vs organic shading
- 15:09→16:22 [CustomSDF][FractalSDF] · in-depth · scripted · 88% — the CustomSDF live-coding playground: porting GLSL fractals to HLSL, A/B/C/D variables, presets, and preset blending
- 16:22→17:43 [FieldVolumeForce][ParticleSystem][TorusSDF][TransformField][PerlinNoise3] · in-depth · scripted · 82% — driving particles with a field volume force from a torus field, animating it via transform-field + Perlin noise, combining with a directional force
- 17:43→19:02 [SelectPointsWithSDF][SphereSDF][AddNoise] · in-depth · scripted · 80% — selecting points by SDF into fx1/fx2 attributes to mask a noise displacement on instanced cubes; mapping/repeat options
- 19:02→19:21 [MoveToSDF][TorusSDF] · explained · scripted · 82% — moving points onto an SDF surface (e.g. a torus)
- 19:21→20:13 [ui:ShaderGraph] · passing · scripted · 65% — roadmap: extending the SDF system into a full node-based shader/material graph
