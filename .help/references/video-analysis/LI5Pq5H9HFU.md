---
video: LI5Pq5H9HFU
type: tutorial
title: "Hands-On 002: Creating a 3D Animated Brick Ocean"
duration: 0:22:04
---

A hands-on build of a "brick ocean" scene: a 100×100 grid of beveled-cube bricks displaced by noise into a rolling ocean, plus a glowing cube driven by a shared locator, finished with image-based lighting and a post-processing stack (SSAO, depth of field, color grade, glow, chromatic aberration).

## Mentions
- 1:35→1:53 [LoadObj] · passing · scripted · Example · 70% — Pulling in a bundled mesh asset (here a beveled cube) as the building block for an instanced scene before any displacement or shading.
- 1:53→2:11 [SetMaterial] · passing · scripted · Example · 70% — Assigning a very bright material early so an object reads as a light-emitter once glow post-processing is added.
- 2:19→2:53 [GridPoints] · explained · scripted · Parameters · 88% — Set one axis to a single point and the other two to 100 each to get a flat 100×100 sheet of points; scale the whole grid up to size the surface you'll instance onto.
- 2:34→2:45 [DrawPoints] · passing · scripted · Tip · 80% — Temporarily wire it in (and pin the output) just to visualize a generated point set while you tune the generator feeding it.
- 3:02→3:44 [FractalNoise] [SamplePointAttributes_v1] · in-depth · scripted · Example · 85% — Feed a noise field into the attribute sampler to push point positions along a single axis, turning a flat grid into a rolling surface; rotate the noise 90° and raise its scaling so the pattern aligns with the grid plane.
- 4:42→5:20 [FractalNoise] · explained · scripted · Parameters · 78% — Offset and warp-offset values shift where the noise samples, and animating those (rather than the underlying field) is enough to make the displaced surface drift like water.
- 5:20→5:55 [Time] · passing · scripted · Tip · 70% — Drive an animation by wiring a running time value into a noise offset so the displacement evolves every frame.
- 6:08→6:34 [DrawMeshAtPoints] · explained · scripted · Example · 86% — Swap raw points for actual geometry by instancing a mesh at every point; the point set supplies position while a separate mesh input supplies the shape stamped at each one.
- 6:42→7:30 [CylinderMesh] [DrawMesh] · explained · scripted · Example · 80% — Build a small cylinder, reduce its segment detail and shrink its radius/height, then draw it stacked on a cube to compose a Lego-style brick from primitives.
- 7:41→8:16 [Group] · explained · scripted · Concept · 80% — Combining several meshes under one group yields a single combined mesh you can instance as a unit; reuse the same point set twice (one per sub-mesh) to scatter the composite.
- 8:16→8:46 [DrawMeshAtPoints] · passing · scripted · Gotcha · 70% — Instanced meshes inherit the point grid's scale, so an oversized result usually means dialing the per-instance size down rather than touching the points.
- 8:46→9:05 [SetMaterial] [SetEnvironment] · explained · scripted · Example · 80% — Pair a material with an environment so the surface picks up image-based lighting from a surrounding texture instead of looking flat.
- 9:38→9:55 [Transform] · passing · scripted · Example · 70% — Drop a transform into a sub-graph to reposition and scale an element independently before animating it.
- 9:55→10:33 [Locator] · in-depth · scripted · Concept · 88% — A single animated locator acts as one shared position source you can plug into multiple consumers (a mesh and a light) so they always stay co-located.
- 10:33→11:05 [PerlinNoise] · explained · scripted · Example · 85% — Wire a noise generator into a locator's position to make an object wander smoothly through space rather than follow a fixed path.
- 11:11→11:55 [PerlinNoise] · explained · scripted · Parameters · 82% — Tune the per-axis amplitude to constrain wandering motion to a plane — drop the vertical amplitude so the object circles rather than bobs up and down.
- 11:55→12:55 [PerlinNoise] [Multiply] · in-depth · scripted · Parameters · 82% — For spin, feed noise into rotation and multiply its ±1 range up to 360°; lower frequency for slower turns and reduce octaves so the rotation holds a steady speed instead of jittering.
- 13:03→13:46 [SetPointLight] · in-depth · scripted · Parameters · 85% — Raising decay shrinks the lit area and intensity brightens it; recolor the light for mood — useful for an emissive object that should cast its own colored glow.
- 13:46→14:01 [SetPointLight] [Locator] · explained · scripted · Example · 84% — Drive a light's position from the same locator that moves its host object so the light tracks the object exactly.
- 14:34→15:01 [TextureToCubeMap] · in-depth · scripted · Gotcha · 88% — Always convert an environment image into a cube map before feeding it as the lighting environment — skipping this step can crash the app.
- 14:01→14:34 [AdjustColors] · passing · scripted · Tip · 68% — Lowering exposure and saturation on an environment image tones down image-based lighting before it's converted for the scene.
- 15:08→15:36 [SetEnvironment] · explained · scripted · Gotcha · 80% — After assigning an environment texture you must trigger an "update live once" so the lighting actually samples the new cube map.
- 15:49→16:23 [OrbitCamera] · explained · scripted · Example · 82% — Frame an instanced scene by orbiting around its center, then lower the camera to find a flattering angle on the surface.
- 16:23→16:58 [SetMaterial] · explained · scripted · Tip · 78% — A faint dark-blue emissive on a neutral material reads as distant water — a small emissive tint shifts perceived color without lighting the scene.
- 16:58→17:33 [DepthOfField] · in-depth · scripted · Parameters · 85% — Needs the depth buffer wired in; set focus distance manually and raise the amount so foreground subjects drift in and out of focus.
- 17:33→17:55 [Locator] · passing · scripted · Tip · 70% — Instead of a hand-tuned focus distance, feed a moving locator's camera-space distance into depth-of-field so focus tracks a subject automatically.
- 18:09→18:51 [ColorGrade] · explained · scripted · Tip · 80% — Where most of a scene's final look is set — start from a preset or hand-tune curves to push the overall mood after geometry and lighting are done.
- 18:44→19:18 [SSAO] · in-depth · scripted · Concept · 86% — Adds contact shadows in the crevices between geometry, giving instanced/cluttered scenes much more depth; tame it by lowering opacity or its boost settings if the darkening is too strong.
- 19:45→20:32 [Glow] · explained · scripted · Gotcha · 80% — Its default strength is deliberately very intense, so expect to dial it down — this is what makes bright/emissive materials bloom.
- 20:32→21:03 [DepthOfField] · passing · scripted · Parameters · 75% — Reduce the amount when the blur overwhelms the frame, and lean on the environment's background blur to soften distant areas separately.
- 20:49→21:19 [SetEnvironment] · explained · scripted · Parameters · 76% — An advanced "background blur" option blurs the environment backdrop independently of the depth-of-field on the geometry.
- 21:03→21:31 [SetPointLight] · passing · scripted · Parameters · 74% — Push intensity up and decay down together to make a colored light reach further across the scene.
- 21:31→21:51 [ChromaticAbberation] · passing · scripted · Tip · 75% — A final color-fringing pass at the edges to lend the rendered image a lens-like finish.
