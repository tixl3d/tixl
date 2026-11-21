# DepthAnything

*in [Lib.io.video.depthanything](README.md)*

Estimates a depth map from an image with Depth Anything 3 or 2 through ONNX Runtime.

Runs the model on a background worker, so the graph never waits for inference. The image is resampled and normalized on the GPU and only the small model-sized input is read back, which keeps latency low for live input. Models are loaded from the package Assets folder; the scripts in the package Scripts folder download them.

Outputs:
- OutputTexture: The input texture, passed through.
- DepthTexture: Raw model output as a single float channel; DA3 gives depth (far is larger).
- NormalizedDepthTexture: Depth normalized to 0..1 with near bright and visualized according to OutputFormat.
- ConfidenceTexture: Per-pixel confidence, for models that predict it.
- SkyTexture: Sky score, for models that predict it. Metric Large does.
- UpdateCount: Increments each time a new depth frame was processed.
- MinDepth, MaxDepth: Depth range of the latest frame, settled over time by Smoothing.
- VerticalFov: Field of view in degrees estimated by the model, 0 when it gives none.
- InferenceMs, LatencyMs: Model time and the time from capturing the input to receiving its result.
- Status: Current model and execution provider state.

Tips:
- Use CUDA or DirectML and a Small model for live input; the CPU is only practical for stills.
- Feed VerticalFov and DepthTexture into DepthToPosition for a point-cloud style reconstruction.
- A lower InputResolution trades depth detail for speed on models that accept it.

AKA: depth estimation, depth anything, depth anything 3, da3, monocular depth, z-depth, ai

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **ExecutionProviderParam** (Int32) | Hardware used for inference. The dropdown lists only the providers this operating system supports: Windows has DirectML, CUDA and TensorRT, Linux has CUDA and TensorRT, macOS has CoreML. A provider that cannot initialize falls back to the next one, ending at CPU. |
| **InputTexture** (Texture2D) | The image to estimate depth for. Any texture format and size works; it is resampled on the GPU. |
| **Enabled** (Boolean) | Enables or disables depth estimation. |
| **MatchInputResolution** (Boolean) | Scales the depth outputs to the input resolution on the CPU. Leave off for the lowest latency; the GPU upsamples the model-sized depth when it is sampled. |
| **ModelSizeParam** (Int32) | Model variant: V3 Small, Base, Large, Mono Large, Metric Large, Giant, Nested Giant. Larger models are more detailed and slower; Metric Large predicts depth in meters. Legacy V2-era values run their DA3 equivalent. |
| **Normalization** (Int32) | Visualization range: Full Range stretches the frame min/max; Percentile bounds the range at the 2nd/98th quantiles so outlier spikes do not compress the foreground. |
| **SkyHandlingParam** (Int32) | Stabilize (default) clamps sky depth to the foreground maximum and excludes sky from the range, so sky reads as consistently far; Keep publishes the raw model output. |
| **OutputFormat** (Int32) | Visualization of NormalizedDepthTexture. |
| **EnhanceContrast** (Boolean) | Stretches the depth range of the visualization. |
| **InvertDepth** (Boolean) | Flips near and far. |
| **Debug** (Boolean) | Logs detailed timing and state information. |
| **InputResolution** (Int32) | Size the image is scaled to before inference. Auto uses the model default. Models exported at a fixed size ignore it. |
| **Smoothing** (Single) | Blends each result with the previous one and settles the depth range, which calms the flicker of the model's frame-to-frame scale. 0 is off; higher values trail fast motion. |
| **FieldOfView** (Single) | Horizontal field of view of the source camera in degrees. Only the Metric model uses it, to convert its output to meters. |

## Outputs

| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |
| **DepthTexture** | T3.Core.DataTypes.Texture2D |
| **NormalizedDepthTexture** | T3.Core.DataTypes.Texture2D |
| **ConfidenceTexture** | T3.Core.DataTypes.Texture2D |
| **SkyTexture** | T3.Core.DataTypes.Texture2D |
| **UpdateCount** | System.Int32 |
| **MinDepth** | System.Single |
| **MaxDepth** | System.Single |
| **VerticalFov** | System.Single |
| **InferenceMs** | System.Single |
| **LatencyMs** | System.Single |
| **Status** | System.String |
| **CamReference** | System.Object |


