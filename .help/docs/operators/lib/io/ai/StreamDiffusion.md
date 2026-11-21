# StreamDiffusion

*in [Lib.io.ai](README.md)*

Realtime video-to-video diffusion with the StreamDiffusion V2 causal Wan2.1 model on ONNX Runtime. Every frame of the connected video or camera input is restyled with the prompt; the output size comes from the export.

Produce the ONNX export once with the scripts in Operators/StreamDiffusion/tools/streamdiffusionv2; the checkpoint download in the model browser (Settings > AI Models) provides model.pt, the T5 text encoder and the tokenizer.

Tips:
- Connect a video or camera to `InputImage` - the model is video-only and cannot generate from text alone.
- `Model` picks the export from the model store (Auto = first valid); models are installed in Settings > AI Models.
- `Steps` is the KV-cache batch size (1-4); every extra step doubles the cache.
- `Strength` is how strongly each frame is re-noised: lower keeps more of the previous frame.
- `FastDecode` decodes one latent instead of a three-latent window: faster, less temporal smoothing.
- `Feedback` blends the previous output frame to smooth flicker.
- Use a CUDA or TensorRT GPU; DirectML and CPU work but are far slower.

AKA: ai, generation, diffusion, onnx, streamdiffusion, streamdiffusion v2, video diffusion, wan, video to video, restyle

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Model** (String) | Which export in the model store to run. Auto picks the first valid one; the dropdown lists everything Settings > AI Models has installed. Models are managed in the AI settings, never as paths here. |
| **CudaDevice** (Int32) | DirectML device index for multi-GPU systems. Shows available adapters in dropdown. |
| **TriggerGenerate** (Boolean) | Momentarily true to trigger a single generation when AutoGenerate is off. |
| **FastDecode** (Boolean) | V2 only: decode one latent instead of a three-latent window. Saves about 20 ms per frame on TensorRT with slightly less temporal smoothing. |
| **ResetStream** (Boolean) | Momentarily true to drop the rolling KV cache and re-prime the stream from the next input frame - useful on scene cuts or when the style drifts. |
| **TargetFps** (Single) | Paces the stream at this many frames per second (default 16, matching capture and downstream). 0 runs as fast as the GPU allows; identical input frames cost nothing at any rate. |
| **MatchInputResolution** (Boolean) | Stretches the model output (e.g. 256x256) back over the input image dimensions, so the output matches the source aspect ratio and pixel size for compositing. |
| **Prompt** (String) | Text prompt describing the desired image. Be descriptive for better results. |
| **AutoGenerate** (Boolean) | Continuously regenerate when inputs change. Disable for manual trigger mode. |
| **ExecutionProviderParam** (Int32) | Hardware used for inference. The dropdown lists only the accelerations that can actually run this model on this operating system: Windows and Linux offer CUDA and TensorRT, macOS offers CoreML. DirectML cannot run the V2 model (its DiT/VAE 5D reshapes fail on the DML provider), and a saved DirectML choice snaps to CUDA. A provider that cannot initialize falls back to the next one, ending at CPU. |
| **Strength** (Single) | How strongly each Stream frame is re-noised (default 0.8): lower keeps more of the previous frame, higher follows the input faster; the motion-aware controller lowers it automatically on fast motion. |
| **Steps** (Int32) | The KV-cache batch size (default 2): 1 is fastest, 2-4 add temporal context at twice the cache per step. Changing it restarts the stream. |
| **Debug** (Boolean) | Logs detailed timing and state information. |
| **InputImage** (Texture2D) | Source image for img2img and Stream mode. Ignored in txt2img mode. Accepts RGBA images and float render targets; linear float values are encoded to sRGB before generation. |
| **ResizeMode** (Int32) | How the input image fits the output: Stretch = fill, CenterCrop = crop then fill, Pad = fit with borders. |
| **Seed** (Int32) | Random seed for generation. -1 = random, same value = reproducible output. |

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |
| **OutputSize** | T3.Core.DataTypes.Vector.Int2 |
| **GenerationTime** | System.Single |
| **NoiseScale** | System.Single - effective noise scale after the motion-aware controller |
| **EncodeMs** | System.Single |
| **DenoiseMs** | System.Single |
| **DecodeMs** | System.Single |


