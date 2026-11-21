# Depth Scan 3D Example

Turns a moving camera into a persistent world-space point cloud: DepthAnything estimates
depth AND camera pose per frame, `DepthScan3D` unprojects every valid pixel into the world
and merges it into a voxel grid - scan an object by walking the camera around it.

## Composition Structure

```
[LoadImage / Video] --> [DepthAnything] --> [DepthScan3D] --> [DrawPoints] --> [Output]
                             |                     |
                             +-- DepthTexture ---- into DepthMap
                             +-- OutputTexture --- into ColorMap
                             +-- CamReference ---- into CameraReference
```

## Node Configuration

### DepthAnything
- **ModelSize**: V3 Base or V3 Small (the current exports' extrinsics output does not respond to camera motion, so the cloud builds in camera space either way; see the README)
- **ExecutionProvider**: CUDA or DirectML

### DepthScan3D
- **DepthMap**: `DepthAnything.DepthTexture`
- **ColorMap**: `DepthAnything.OutputTexture`
- **CameraReference**: `DepthAnything.CamReference`
- **VoxelSize**: 0.02 for room-scale metric scans; 0.1 for large scenes
- **Mode**: Average (denoises overlapping views)
- **PixelStride**: 2-4 for live scanning, 1 for final passes
- **StartRecord / StopRecord**: buttons or timeline keys; **SampleLifetime**: 0 to keep everything
- **MaxPoints**: 500k default (raise for dense scans)

### DrawPoints
- **GPoints**: `DepthScan3D.Points`
- **PointSize**: 1-2

## Expected Results

- A cloud that grows as you move the camera and holds when recording stops.
- Overlapping views denoise through the voxel average; Coverage-style confidence gating drops flying pixels.
- Save produces a .ply that opens directly in MeshLab / CloudCompare / Blender.

## Tips

- VoxelSize is the min-distance knob: smaller = more detail, larger = stronger merge.
- The current export's pose output is inert, so camera motion is not fused into world space; a fixed camera with a turntable (single-viewpoint) is the reliable scanning setup.
- Without CameraReference the cloud matches DepthScanBuffer's camera-space output.
