# DepthToPoints Operator

## Overview
Unprojects a depth map into a camera-space point buffer on the GPU. Each point carries the color sampled at its pixel and a world-space `Scale` that covers a fixed number of screen pixels at its depth. Connect the buffer to `DrawPoints` with `UsePointsScale` enabled to render it as live depth splats.

The work stays on the GPU: there is no readback and no compaction pass. Invalid depths and points below the confidence threshold become zero-scale transparent points that `DrawPoints` discards.

## Inputs
- **Texture2D ColorTexture** - Color sampled for each point; white when unconnected
- **Texture2D DepthTexture** - Source depth (R32_Float); V3 depth or V2 disparity
- **Texture2D ConfidenceTexture** - Optional confidence map; points below `ConfidenceThreshold` are dropped
- **float FieldOfView** - Vertical field of view in degrees (default: 50; 0 falls back to 50)
- **float DepthScale** - Scales the distance of every point (default: 1)
- **int PixelStride** - Sample every Nth pixel (default: 2)
- **int MaxPoints** - Upper bound for the buffer (default: 262,144); the stride grows when needed
- **float SplatSizePixels** - Screen-space splat diameter in pixels (default: 2)
- **float Opacity** - Alpha of each point (default: 1)
- **float TemporalSmoothing** - Blend each point with the previous frame (0 = raw, 0.7-0.9 for a still camera; default: 0.5)
- **float ConfidenceThreshold** - Minimum confidence to keep a point (default: 0; around 1.0 with the V3 Base confidence map)
- **bool Enabled** - Toggle point generation

## Outputs
- **BufferWithViews Points** - Point buffer for `DrawPoints`
- **int PointCount** - Number of points in the buffer, including dropped points

## Stabilization
`TemporalSmoothing` keeps the previous point buffer and blends position, scale and color into the new frame. A still webcam benefits from 0.7-0.9; lower the value (or use 0) when the camera or subject moves, since the blend adds lag. The history buffer doubles the point-buffer memory. For spatial denoising, feed `DepthFilter` before this operator; `DepthAnything.Smoothing` blends the depth before unprojection. With the V3 Base model, raising `ConfidenceThreshold` to about 1.0 removes the low-confidence flying pixels that otherwise become long spikes.

## DrawPoints Setup
- Enable **UsePointsScale**
- Set **PointSize** to about 5 for a 1:1 `SplatSizePixels` mapping
- Disable **EnableZWrite** so overlapping transparent splats don't occlude each other
- Optional: connect a soft dot texture to `Texture_` for round splats

## Use Cases
- Live depth splat of a camera or video feed
- Orbitable 2.5D reconstruction from a single image
- Point-cloud visualization with per-point colors
- Feeding point-based effects from a depth map
