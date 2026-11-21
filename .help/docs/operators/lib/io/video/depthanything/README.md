# Lib.io.video.depthanything

## Operators

- [**DepthAnything**](DepthAnything.md) — Estimates a depth map from an image with Depth Anything 3 or 2 through ONNX Runtime.
- [**DepthAnythingExample**](DepthAnythingExample.md) — Estimates a depth map from an image with Depth Anything 3. Near objects stay bright; switch ModelSizeParam for a larger model or InvertDepth to flip the ramp. The colorized NormalizedDepthTexture is the output.
- [**DepthAnythingExampleNormalSurface**](DepthAnythingExampleNormalSurface.md) — Turns a depth map into a normal map and camera-space positions. Wire the normal output into lighting or relighting setups; the position texture holds x, y and distance for point clouds or displacement.
- [**DepthAnythingExampleParallaxMove**](DepthAnythingExampleParallaxMove.md) — Shifts a still image by its depth, as if the camera moved sideways. Animate Offset for the classic 2.5D wobble; FocalPlane chooses the depth that stays still. MinDepth and MaxDepth come from DepthAnything.
- [**DepthAnythingLiveExample**](DepthAnythingLiveExample.md) — Estimates depth from a live network stream: a VideoStreamInput connects to an RTSP camera and Depth Anything 3 runs on every frame it delivers. Output is the colorized NormalizedDepthTexture (near bright). Point StreamUrl at your own camera; VideoStreamInput's Status output shows the connection state.
- [**DepthAnythingSplatExample**](DepthAnythingSplatExample.md) — Renders a live video stream as a navigable 2.5D point cloud: Depth Anything 3 estimates per-pixel depth, DepthToPoints unprojects it into camera-space splats (size and opacity from depth and confidence), and DrawPoints billboards them into a RenderTarget. Orbit or move the Camera to look around the scene. Stabilize a stationary camera with DepthToPoints.TemporalSmoothing; rebuild from stills with TemporalSmoothing 0.
- [**DepthFilter**](DepthFilter.md) — Smooths a depth map with an edge-aware bilateral filter.
- [**DepthParallax**](DepthParallax.md) — Shifts an image by its depth, as if the camera moved sideways.
- [**DepthThreshold**](DepthThreshold.md) — Creates a mask from the depth range between two thresholds.
- [**DepthToNormal**](DepthToNormal.md) — Converts a depth map into a normal map.
- [**DepthToPoints**](DepthToPoints.md) — Unprojects a depth map into a camera-space point buffer.
- [**DepthToPosition**](DepthToPosition.md) — Unprojects a depth map into camera-space positions.

---

*Auto-generated from the operator library.*
