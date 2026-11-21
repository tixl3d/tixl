# DepthAnythingLiveExample

*in [Lib.io.video.depthanything](README.md)*

Estimates depth from a live network stream: a VideoStreamInput connects to an RTSP camera and Depth Anything 3 runs on every frame it delivers. Output is the colorized NormalizedDepthTexture (near bright). Point StreamUrl at your own camera; VideoStreamInput's Status output shows the connection state.

## Outputs

| Name | Type |
|---|---|
| **Depth** | T3.Core.DataTypes.Texture2D |


