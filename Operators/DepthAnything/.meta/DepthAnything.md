# DepthAnything Operator

## Overview
Estimates depth from images with Depth Anything 3 through ONNX Runtime. The input is resampled and normalized on the GPU, inference runs on a worker thread, and the latest result is published when it is ready.

## Inputs
- **Texture2D InputTexture** - Source image or video texture, any format and size
- **bool Enabled** - Toggle processing on/off
- **ModelSize** - V3 Small, Base, Large, Mono Large, Metric Large, Giant, Nested Giant; legacy V2-era values run their DA3 equivalent
- **Resolution** - Inference size for models with dynamic input. Fixed-size exports ignore it
- **ExecutionProvider** - Only the providers this OS supports are offered: Windows CPU/DirectML/CUDA/TensorRT, Linux CPU/CUDA/TensorRT, macOS CPU/CoreML; a saved value the OS cannot run snaps to the closest one
- **bool MatchInputAspect** - Stretches a square model's output to the input's aspect ratio at model resolution; aspect-matched exports (e.g. da3-small-392x224-fp16.onnx for 16:9) need no resize
- **int CameraMotion** - How the physical camera moves, for the CamReference pose: Free (hand-held), RotationOnly (pan/tilt or tripod - no position drift), Static (fixed reference pose)
- **float Smoothing** - Temporal blend of depth and depth range
- **float FieldOfView** - Horizontal field of view in degrees; used by the Metric model
- **DepthOutputFormat OutputFormat** - Grayscale/Color/Rainbow visualization
- **DepthNormalization Normalization** - Full Range stretches the frame min/max; Percentile bounds the range at the 2nd/98th quantiles against outlier spikes
- **SkyHandling** - Stabilize (default) clamps sky depth to the foreground maximum and excludes sky from the range; Keep publishes raw model output
- **bool EnhanceContrast, InvertDepth, Debug**

## Outputs
- **Texture2D OutputTexture** - Original input (passthrough)
- **Texture2D DepthTexture** - Raw model output (R32_Float); depth (far larger)
- **Texture2D NormalizedDepthTexture** - Visualized depth (RGBA), near bright
- **Texture2D ConfidenceTexture** - Per-pixel confidence, when the model predicts it
- **Texture2D SkyTexture** - Sky score, when the model predicts it
- **int UpdateCount**, **float MinDepth**, **float MaxDepth**
- **float VerticalFov** - Camera field of view in degrees estimated by the model, 0 when unknown
- **float InferenceMs**, **float LatencyMs** - Model time and capture-to-result time
- **string Status**
- **Object CamReference** - Estimated camera as an ICamera: pose from the model's extrinsics and FOV from its intrinsics. Falls back to the reference pose at the origin when the model has no extrinsics output (the Small export is depth-only; Base and some larger exports have the pose).

## Camera Reference
`CamReference` can be connected to camera-reference inputs such as `ApplyCamTransform.CamReference` or `SortPoints.CameraReference`. The pose is in DA3's image frame: +Z forward, image-up axis, position from the extrinsics. The depth splat is built in a y-up, +Z-forward frame, so a render camera driven directly by `CamReference` needs a Y flip to match the splat's orientation.

## Model Files
Place in `Assets/`, or run `Scripts/download_models.py` (`.ps1` on Windows). The first file found wins:
- V3 Small: `da3-small-fp16.onnx`, `da3-small.onnx`, `da3-small-int8.onnx`
- V3 Base: `da3-base-fp16.onnx`, `da3-base.onnx`
- V3 Large: `da3-large-fp16.onnx`, `da3-large.onnx`
- V3 Mono Large: `da3-mono-large-fp32.onnx`, `da3-mono-large-fp16.onnx`, `da3-mono-large-w8a16.onnx`, `da3-mono-large-int8.onnx`
- V3 Metric Large: `da3-metric-large-fp16.onnx`
- V3 Giant / Nested Giant: own export from the official weights (no public ONNX export)

## Low-Latency Notes
- Preprocessing (resample, ImageNet normalization, planar layout) is one compute pass; only the model-sized input crosses to the CPU.
- Only one frame is in flight; a new frame is captured as soon as the previous result arrived, so results are never queued behind stale input.
- **Use the fp16 export.** `da3-small-fp16.onnx` is tried before the fp32 file. In the same run on a throttled laptop RTX 3080 Ti, a 518x518 frame went from 163 ms (fp32) to 56 ms (fp16); at the normal clock the fp16 model takes 28 ms. `Scripts/convert_to_fp16.py` creates the file from an existing fp32 export.
- CUDA is the fastest provider for the uint8 NHWC DA3 Small export: TensorRT's parser rejects the graph's uint8 `Transpose` and assigns it to CUDA. TensorRT is useful for float-NCHW exports such as DA3 Base; the operator still lets CUDA run the nodes TensorRT does not claim.
- int8 exports are meant for the CPU; use fp16 or fp32 files with CUDA, DirectML or TensorRT.
- Depth, filter, normal, position and parallax operators downstream stay on the GPU.
- A laptop GPU running 518x518 inference continuously can reach its thermal limit and drop clocks; `MatchInputAspect` stays at model resolution, so only model-sized depth crosses back either way.
