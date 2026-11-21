# DepthAnythingSplatExample

*in [Lib.io.video.depthanything](README.md)*

Renders a live video stream as a navigable 2.5D point cloud: Depth Anything 3 estimates per-pixel depth, DepthToPoints unprojects it into camera-space splats (size and opacity from depth and confidence), and DrawPoints billboards them into a RenderTarget. Orbit or move the Camera to look around the scene. Stabilize a stationary camera with DepthToPoints.TemporalSmoothing; rebuild from stills with TemporalSmoothing 0.

## Outputs

| Name | Type |
|---|---|
| **Splat** | T3.Core.DataTypes.Texture2D |


