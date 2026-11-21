# Depth Scan Example

Turns a webcam or video into a LiDAR-style held scan: DepthAnything estimates depth,
`DepthScanBuffer` merges frames into one persistent map (optionally with color), and the
held scan drives a point cloud you can orbit and save.

## Composition Structure

```
[LoadImage / Video] --> [DepthAnything] --> [DepthScanBuffer] --> [DrawPoints] --> [Output]
                             |                     |          |
                             +-- DepthTexture -----+          +-- HeldColor (optional pass-through)
                             +-- VerticalFov ----- into FieldOfView
```

## Node Configuration

### DepthAnything
- **ModelSize**: V3 Small
- **ExecutionProvider**: CUDA or DirectML
- **Smoothing**: 0 for scanning; the buffer itself does the denoising

### DepthScanBuffer
- **StartRecord / StopRecord**: wire buttons or timeline keys here instead of holding Capture
- **SampleLifetime**: 0 for a permanent scan; 8-20 for a fading sweep that keeps refreshing where you point
- **PointGrid**: Hexagonal for an even splat spread; **PointStability**: 0.5-0.8 to calm a live scan
- **DepthMap**: `DepthAnything.DepthTexture`
- **ColorMap**: `DepthAnything.OutputTexture` (same size as the depth map)
- **CameraReference**: `DepthAnything.CamReference` (moves the cloud into world space; without it the cloud is camera-space)
- **FieldOfView**: `DepthAnything.VerticalFov` (fallback when no camera is connected)
- **Mode**: Average for a denoised still-camera scan, Latest for a moving camera
- **Capture**: on while scanning; switch off to hold the scan
- **MaxCaptures**: 30-60 for a still-camera scan, 0 (unlimited) for a moving camera
- **PixelStride**: 1-2 (MaxPoints caps the cloud automatically)

### DrawPoints
- **GPoints**: `DepthScanBuffer.PointCloud`
- **UsePointsScale**: false; **PointSize**: 1-2
- **EnableZWrite**: false

## Expected Results

- A held point cloud that keeps its shape when the camera input stops - it holds.
- With SampleLifetime > 0 the scan fades where it stops being observed and Coverage drops.
- Coverage climbs while Capture is on and freezes when it goes off.
- SavedPath fills in after Save; the PNG opens in any image viewer, the raw file keeps exact float32 depth.

## Tips

- Still camera: Mode Average with MaxCaptures ~40 denoises like a multi-take LiDAR scan.
- Moving camera: Mode Latest or Min; the held scan is a snapshot, not a reconstruction of
  several viewpoints (pixel-wise merging assumes one viewpoint).
- The held HeldDepth also feeds DepthToPosition for camera-space geometry, or DepthToNormal
  for a lit look.
