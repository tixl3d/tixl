# Depth Anything Operator Package for TiXL

Real-time monocular depth estimation for TiXL with **Depth Anything 3**, plus GPU operators that turn the depth into normals, positions and parallax.

## Operators

| Operator | Purpose |
|----------|---------|
| `DepthAnything` | Runs the model on a worker thread; outputs depth, visualized depth, confidence, sky, field of view, timing and the estimated camera |
| `DepthFilter` | Edge-aware bilateral smoothing of depth (GPU) |
| `DepthToNormal` | Normal map from depth with a Sobel gradient (GPU) |
| `DepthToPosition` | Camera-space position texture from depth and field of view (GPU) |
| `DepthToPoints` | Camera-space point buffer from depth and color, for live splats with `DrawPoints` (GPU) |
| `DepthParallax` | Shifts an image by its depth for 2.5D camera moves (GPU) |
| `DepthThreshold` | Binary depth mask (still reads depth back on the CPU) |
| `DepthScanBuffer` | LiDAR-style scan buffer: merges depth frames into one held map (with color), rebuilds a drawable point cloud and saves 16-bit PNG / raw float32 |
| `DepthScan3D` | World-space 3D point cloud scanner: unprojects depth with the DA3 camera pose into a voxel-merged, held cloud; saves PLY |

## Depth Scan Buffer

`DepthScanBuffer` turns the per-frame depth output into a held scan, the way a LiDAR
scanner accumulates: while `Capture` is on, every frame is merged into one persistent
map (`Latest`, `Average`, `Min`, `Max`; per-pixel sample counts make `Average` a true
multi-frame average). The held map is published on `HeldDepth` until `Reset`, and it
survives a disconnected input. Optional `Confidence` gating drops unreliable samples;
optional `ColorMap` is held alongside and colors the cloud.

`PointCloud` unprojects the held scan into a CPU-built point buffer that draws on the
D3D11 backend too (unlike the compute-written buffer of `DepthToPoints`, which needs
Vulkan). With `CameraReference` connected (wire `DepthAnything.CamReference`, which
implements `ICamera`) the cloud unprojects into the camera's world space using the pose
and frustum of the capture frame; without it, `FieldOfView` builds a DA3-style
camera-space cloud (+Z forward, like `DepthToPoints`). Feed `HeldDepth` to
`DepthToPosition` for camera-space reconstruction regardless.

`Save` writes the scan as a 16-bit PNG (depth x `SaveScale`; 1000 = millimeters) or as
exact raw float32 with a small header (magic `T3DS`, width, height, captures, valid
pixels, min/max, then row-major float32).

Recording works trigger-based: `StartRecord` / `StopRecord` latch a recording run (the
`Recording` output shows the state), `Capture` stays the manual override. `SampleLifetime`
ages samples out after N seconds without a re-observation, so a scan fades like a decaying
LiDAR sweep; `Coverage`, the masks and the point cloud follow. For stable points while a
live scan keeps updating, `PointGrid` picks Square or Hexagonal sampling (hex rows are
offset by half a stride with sqrt(3)/2 row spacing, so all six neighbors sit at the same
distance) and `PointStability` blends rebuilt point positions toward their previous spots
(0 = exact, 1 = frozen).

## Depth Scan 3D

`DepthScan3D` is the 3D sibling of `DepthScanBuffer`: instead of holding a 2D depth map,
it scans depth into a persistent **world-space point cloud**. Every valid pixel is
unprojected using the camera pose (connect `DepthAnything.CamReference` to
`CameraReference` - DA3 estimates pose and field of view per frame), quantized to a
voxel grid (`VoxelSize` - your min-distance knob), and merged: overlapping observations
are averaged (`Mode: Average`, weighted running mean - the LiDAR-style denoising) or
replaced. Confidence gates samples, `ColorMap` colors them, and `SampleLifetime` ages
points out so the cloud follows what the camera keeps seeing.

Without a camera reference, points land in DA3 camera space (+Z forward), matching
`DepthToPoints`/`DepthScanBuffer` output.

`Save` exports the cloud as a **PLY** file (binary or ASCII - opens in MeshLab,
CloudCompare, Blender); the `Points` output feeds `DrawPoints` directly and draws on
both D3D11 and Vulkan.

## Models

Put the ONNX files in `Assets/`, or run `Scripts/download_models.py` (`Scripts/download_models.ps1` on Windows). Pass file names to download only those, e.g. `python download_models.py da3-small.onnx`.

| Selection | Files tried in order | License |
|-----------|----------------------|---------|
| V3 Small | `da3-small-fp16.onnx`, `da3-small.onnx`, `da3-small-int8.onnx` | Apache-2.0 |
| V3 Base | `da3-base-fp16.onnx`, `da3-base.onnx` | Apache-2.0 |
| V3 Base (camera tracking) | `da3-base-mv2.onnx` (own export, see `Scripts/export_da3_multiview.py`) | Apache-2.0 |
| V3 Base (6-view stream) | `da3-base-mv6-392.onnx` (own export: `export_da3_multiview.py --views 6 --size 392`) | Apache-2.0 |
| V3 Mono Large | `da3-mono-large-fp16.onnx`, `da3-mono-large-int8.onnx` | Apache-2.0 |
| V3 Metric Large | `da3-metric-large-fp16.onnx` | Apache-2.0 |
| V3 Large | `da3-large-fp16.onnx`, `da3-large.onnx` | CC-BY-NC 4.0 |
| V3 Giant | `da3-giant-fp16.onnx`, `da3-giant.onnx` (own export from the official weights) | CC-BY-NC 4.0 |
| V3 Nested Giant | `da3-nested-giant-fp16.onnx`, `da3-nested-giant.onnx` (own export) | CC-BY-NC 4.0 |

The V3 files are community ONNX exports of the official weights. The operator reads the input and output layout from the model and accepts float32 NCHW input (ImageNet normalization is done on the GPU) as well as uint8 NHWC input from exports that normalize inside the graph, such as `da3-small.onnx`. Other exports with these layouts work too: the depth output may be called `depth` or `predicted_depth`, and `depth_conf`, `sky`, `intrinsics` and `extrinsics` outputs are used when present. The single-view V3 Base export includes an `extrinsics` output, but it is the reference identity for every frame (a 30 deg camera rotation returns the same matrix as the anchor frame). Camera motion needs the two-view export (`V3 Base (camera tracking)`, see "Camera Reference"). The Giant and Nested-Giant any-view checkpoints are CC-BY-NC 4.0 and have no public ONNX export; the model browser lists them as own-export entries.

Fixed-size exports (for example 504x504 or 504x378) dictate their input size; `InputResolution` only affects models with a dynamic input.

## Latency

- The bundled `da3-small.onnx` is an fp32 export. Convert it once with `Scripts/convert_to_fp16.py` (or use the fp16 file already in `Assets/`): the operator tries `da3-small-fp16.onnx` first. fp16 halves the weights and uses the GPU's fp16 tensor cores. In the same run on a thermally throttled RTX 3080 Ti Laptop, model-only latency for a 518x518 frame fell from 163 ms (fp32) to 56 ms (fp16), with depth correlation 0.999997 and a mean normalized error of 0.0007 against fp32; at the GPU's normal clock the fp16 model takes 28 ms.
- CUDA is the fastest provider for the uint8 NHWC DA3 Small export. TensorRT's parser rejects the graph's uint8 `Transpose` input and assigns that node to CUDA, so plain CUDA is the better choice for this file. TensorRT is useful for float-NCHW exports such as DA3 Base; the operator still lets CUDA run the nodes TensorRT does not claim.
- Resampling to the model size, ImageNet normalization and the planar layout are one compute pass on the GPU. Only the model-sized input is read back, whatever the resolution of the source, and any texture format is accepted.
- One frame is in flight at a time and a new one is captured as soon as the last result arrived, so results never queue behind stale input. `LatencyMs` reports capture to result, `InferenceMs` the model alone.
- The session is created from the file path with the provider chain CUDA, TensorRT or CoreML (macOS), then CPU. TensorRT builds a cached engine under `%LOCALAPPDATA%\TiXL\trt_cache\<model>` — the first load of a model takes a minute or two while the engine builds, after which it starts instantly.
- Measured on an RTX 3080 Ti Laptop, V3 Small at 518x518, single frame: **TensorRT ~6 ms (fp16 engines - fp32 exports like `da3-small.onnx` run at roughly half their fp32 engine latency), CUDA ~31 ms, CPU ~970 ms**. TensorRT wins; CUDA is the good compromise when you want no engine-build wait. A provider that is unavailable falls through to the next one in the chain with a log line naming the reason.
- On Linux (GTX 1070, Vulkan) the operator was verified end to end as well: **CUDA 93 ms, CPU ~1475 ms**, and the splat chain renders. CUDA needs the toolkit's runtime libraries (cuBLAS, cuDNN 9, and the CUDA 13 runtime on the shared ONNX Runtime build); the operator finds them in `/usr/local/cuda*`, `CUDA_PATH` or a `CudaRuntime` folder next to the package even when they are not in `ldconfig`. TensorRT on Linux needs version 10 (`libnvinfer.so.10`) — a TensorRT 8 install is rejected and the chain falls back, so prefer CUDA there.
- DepthAnything and StreamDiffusion share the **same ONNX Runtime 1.30 GPU build**, so both operators run CUDA/TensorRT in one session. The version must match because Windows serves every native import of the module name `onnxruntime.dll` from whichever copy loaded first; mixed versions used to fail provider initialization with Error 1114 and silently fall back to CPU. The 1.30 build needs CUDA 13 + cuDNN 9 and the 580+ driver; Pascal GPUs (like the Linux test box's GTX 1070) no longer get the CUDA provider and run on CPU. DirectML was removed with this change — ORT 1.28 dropped its build; saved DirectML settings run on CUDA instead.
- `Smoothing` blends consecutive results and settles the depth range. DA3 predicts depth up to an unknown scale per frame, so this is what keeps a live visualization from pulsing.
- int8 files are small and meant for the CPU. Use fp16 or fp32 files with CUDA or TensorRT. TiXL discovers the TensorRT 10 runtime in the standard NVIDIA install, `TENSORRT_ROOT`, or a `TensorRTRuntime` folder next to the editor and preloads it; no `PATH` change is needed.

## Depth Semantics

DA3 predicts depth (far is larger). `NormalizedDepthTexture` is near bright. The Metric model returns meters when `FieldOfView` matches the source camera; like everywhere else in this operator it is the vertical field of view in degrees. The metric export takes a fixed square 504 px input, so a 16:9 frame is squeezed to fit.

## Live Depth Splats

`DepthToPoints` turns the depth map into a point buffer and lets TiXL's existing `DrawPoints` render it as camera-facing splats:

```
DepthAnything.DepthTexture      → DepthToPoints.DepthTexture
DepthAnything.OutputTexture     → DepthToPoints.ColorTexture
DepthAnything.VerticalFov       → DepthToPoints.FieldOfView
DepthAnything.ConfidenceTexture → DepthToPoints.ConfidenceTexture   (optional)
DepthToPoints.Points            → DrawPoints.GPoints
```

On `DrawPoints` enable `UsePointsScale`, set `PointSize` to about 5, and turn `EnableZWrite` off. Each point is billboarded in camera space; its `Scale` is the world-space size that covers `SplatSizePixels` screen pixels at its depth, so near and far points look evenly dense. The result is a live 2.5D splat of the input that can be orbited, transformed and composited like any other 3D object.

> **Run splats on the Vulkan backend.** The D3D11 backend does not draw point buffers written by a compute shader, so on D3D11 the chain is silently black although `PointCount` reports tens of thousands of points. Start the editor with `TIXL_BACKEND=vulkan` (or pick Vulkan in the settings) for this chain; D3D11 is fine for every other depth operator. Vulkan needs a Slang SPIR-V build with `spirv-opt` reachable from `slangc`; where the installed release has no `spirv-opt`, put it in Slang's `bin` folder and set `TIXL_SLANG_IN_PROCESS=0` so the compiler can find it.

- `PixelStride` and `MaxPoints` bound the cost. The default 2 and 262,144 points turn a 518x518 model output into ~67k points; the stride grows automatically when the cap would be exceeded.
- Invalid depths and points below `ConfidenceThreshold` are written as zero-scale transparent points, so no compaction pass or CPU readback is needed.
- `VerticalFov` is 0 when the model doesn't predict intrinsics; `DepthToPoints` falls back to 50° so the view stays usable.
- `TemporalSmoothing` blends each point with the previous frame's buffer. 0 is raw and most responsive; 0.7-0.9 calms a still webcam, lower it when the camera or subject moves. It keeps a second point buffer, so the point memory doubles.
- With the V3 Base confidence map, `ConfidenceThreshold` around 1.0 drops the flying-pixel streaks that cause long spikes; the useful value is model-dependent.
- For spatial denoising, put `DepthFilter` between the depth output and `DepthToPoints`; `DepthAnything.Smoothing` blends the depth and its range before unprojection.
- When the source aspect differs from the model's square input, scale the point object's X axis to the source aspect (or enable `MatchInputAspect`, or install an aspect-matched export such as `da3-small-392x224-fp16.onnx`).
- For true Gaussian splatting (learned scale, rotation, opacity and SH), DA3's Gaussian head lives only in the Giant/nested checkpoints, which are CC-BY-NC 4.0; `DepthToPoints` is an Apache-2.0-friendly approximation built from depth and color.

See `Examples/DepthSplat.example.md` for the full node settings and two captures (front and rotated 35°).

## Camera Reference

The community single-view exports predict camera intrinsics (a usable per-frame field of view), but their `extrinsics` output is the reference identity: a single view has no motion to measure. `DepthAnything.CamReference` publishes the camera: the FOV comes from the intrinsics, and the pose comes from the best source available (the `Status` output names it): the model's own extrinsics when they move, otherwise the depth-sequence tracker, otherwise the reference pose at the origin. Connect it to camera-reference inputs such as `ApplyCamTransform.CamReference` or `SortPoints.CameraReference`.

The **V3 Base (camera tracking)** model is a two-view export of the official DA3 Base weights. The operator feeds it the previous and the current frame and reads the current frame's pose relative to the previous one, then chains these into a world pose that `CamReference` publishes (`Status` shows "multi-view"). The depth scale of each pair is arbitrary, so the previous frame's depth in the new pair is compared with its depth in the last pair and the world scale is carried along; the world units are those of the first pair. Rotation tracks well in synthetic tests; translation magnitude is relative and drifts over long sequences, and static input gives zero motion. Switching model or execution provider restarts the pose at the origin. The model costs about twice a single-view Base run.

The **V3 Base (6-view stream)** model infers six frames together, so their depth shares one scale and their poses are consistent inside the window (the idea behind DA3-Streaming). Each new keyframe runs the window of the last five keyframes plus the current frame; the window is placed on the previous one with the frames they share (rotation from their camera poses, scale and translation from a trimmed similarity fit on the unprojected overlap), and the newest frame's depth and pose are published through `DepthTexture` and `CamReference` like the two-view model. A frame that barely differs from the newest keyframe does not enter the window. One window costs about 0.3 to 0.5 s on an RTX 3080 Ti. For a camera that only rotates, DA3's relative rotation is smooth but about 0.7 times the true angle and pitch can drift slowly; use `AxisPtzCamera` for the pose of a pan/tilt head and this model for a camera that moves.

The pose follows DA3's image frame. The depth splat is built in a y-up, +Z-forward frame, so a render camera driven directly by `CamReference` needs a Y flip (or use a `Camera` operator for the view). With a single-view export and no usable depth tracking the pose stays at the reference pose at the origin.

## Examples

The Examples package ships three example compositions (symbol browser → Examples → Lib/io/video/depthanything):

- `DepthAnythingExample` — LoadImage → DepthAnything, showing the colorized normalized depth.
- `DepthAnythingExampleNormalSurface` — the depth feeds DepthToNormal and DepthToPosition, with both textures as outputs.
- `DepthAnythingExampleParallaxMove` — LoadImage and depth feed DepthParallax for a 2.5D camera move; animate `Offset` for the wobble.

They default to the Depth Anything 3 Small model, so they run as soon as `da3-small.onnx` is installed.

## Credits

- [Depth Anything 3](https://github.com/ByteDance-Seed/Depth-Anything-3) by ByteDance Seed
- ONNX Runtime by Microsoft
