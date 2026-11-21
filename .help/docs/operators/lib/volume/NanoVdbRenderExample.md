# NanoVdbRenderExample

*in [Lib.volume](README.md)*

[NanoVdbRender] raymarches three NanoVDB grids at once: a reflective liquid, smoke and fire. Each starts as a [VolumePrimitive] - a slab, a ball and a capsule - roughened once by [VolumeNoise] and uploaded to the GPU with [VolumeToNanoVdb]. The motion comes from the renderer itself: its procedural noise, driven by [Time], makes the smoke billow and the flames flicker without rebuilding a grid.

AKA: smoke, fire, flames, liquid, water, nanovdb, gpu volume, raymarch, glass

Tips
- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.
- Level sets store signed distance in world units (negative inside); fog volumes store density.
- All three grid inputs must be connected; LiquidAmount, SmokeDensity and FireIntensity switch them on and off.
- StepCount trades quality against GPU time.
- Animating a grid on the CPU (a moving noise Offset, say) rebuilds and uploads it every frame; prefer the renderer's Time-driven noise for motion.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


