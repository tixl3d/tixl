# NanoVdbRender

*in [Lib.volume.render](README.md)*

Renders NanoVDB liquid, smoke and fire inside a glass box.

The shader traverses the NanoVDB tree uploaded by [VolumeToNanoVdb] directly on the GPU. The liquid grid is shaded as a reflective surface; the smoke and fire grids use the depth below their zero level as density and temperature, broken up by procedural noise. Place the op inside a camera such as [OrbitCamera] and render it into a texture.

AKA: NanoVDB raymarch, volume render, fluid box

Tips
- Connect all three grid inputs; use LiquidAmount, SmokeDensity and FireIntensity at 0 to hide one of them.
- Grids are rebuilt on the CPU, so keep voxel sizes moderate for animated volumes.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Fire** (ShaderResourceView) | NanoVDB level set whose inside is rendered as hot, emissive fire. |
| **NoiseAmount** (Single) | How strongly procedural noise breaks up smoke and flames. |
| **SmokeDensity** (Single) | Opacity scale of the smoke. 0 hides the smoke. |
| **FireSoftness** (Single) | Depth below the surface in world units over which the flame goes from cool to hottest. |
| **EdgeColor** (System.Numerics.Vector4) | Color and opacity of the glass box edges. |
| **Smoke** (ShaderResourceView) | NanoVDB level set whose inside is rendered as smoke density. |
| **SmokeColor** (System.Numerics.Vector4) | Albedo of the smoke. Alpha is unused. |
| **Liquid** (ShaderResourceView) | NanoVDB level set of the liquid. Connect the ShaderResourceView output of [VolumeToNanoVdb]. All three grid inputs must be connected so the shader registers stay in order; use the amount inputs to switch one off. |
| **StepCount** (Single) | Raymarch steps across the box. Higher is smoother and slower. |
| **SmokeSoftness** (Single) | Depth below the surface in world units over which the smoke fades from transparent to full density. |
| **LightDirection** (System.Numerics.Vector3) | Direction towards the light. |
| **LiquidAmount** (Single) | Opacity of the liquid surface. 0 hides the liquid. |
| **FireIntensity** (Single) | Brightness of the flames. 0 hides the fire. |
| **BoxMax** (System.Numerics.Vector3) | Upper corner of the glass box in world space. |
| **BoxMin** (System.Numerics.Vector3) | Lower corner of the glass box in world space. Rays are clipped to the box. |
| **LiquidColor** (System.Numerics.Vector4) | Body color of the liquid. |
| **Time** (Single) | Drives the scrolling of the procedural smoke and flame detail. |

## Outputs

| Name | Type |
|---|---|
| **DrawCommand** | T3.Core.DataTypes.Command |


