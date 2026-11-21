# ImageSegmentation

*in [Lib.io.video.mediapipe](README.md)*

Segments an image using MediaPipe Image Segmentation.

This operator uses MediaPipe Image Segmentation task to partition input image into different regions (e.g., background, person, hair, clothing). It outputs segmentation masks and data that can be used for compositing or analysis.

Inputs:
- InputTexture: The image to process.
- Enabled: Enables or disables image segmentation.
- Model: Selects the segmentation model to use (e.g., SelfieSegmenter, DeepLabV3).
- SelectedCategories: Comma-separated list of category indices to include in the mask.
- Debug: Enables debug logging and debug texture output.


Outputs:
- OutputTexture: The input texture (passed through).
- MaskTexture: A texture where each pixel value corresponds to a category index.
- UpdateCount: Increments each time a new frame is processed.
- Confidence: The confidence score of the segmentation.
- CategoryMask: A texture where each pixel value corresponds to a category index.
- ConfidenceMask: A texture containing confidence scores for each pixel.
- DebugTexture: Debug visualization texture.

Acceleration selects the inference delegate: Cpu, Xnnpack (multithreaded CPU, the fast default) or Gpu. Gpu needs a native build with the OpenGL delegate (Linux here) and otherwise falls back to Cpu; InferenceThreads sets the Xnnpack worker-thread count (0 = library default).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputTexture** (Texture2D) | The image to process. |
| **Enabled** (Boolean) | Enables or disables image segmentation. |
| **Model** (Int32) | Selects the segmentation model to use (e.g., SelfieSegmenter, DeepLabV3). |
| **SelectedCategories** (String) | Comma-separated list of category indices to include in the mask. |
| **Debug** (Boolean) | Enables debug logging and debug texture output. |
| **Acceleration** (MediaPipeAcceleration) | Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere), Xnnpack = multithreaded CPU (the fast default), Gpu = OpenGL/EGL delegate (Linux; the Windows/macOS natives are CPU-only and fall back to Cpu). |
| **InferenceThreads** (Int32) | Xnnpack worker threads (default 4; 0 = library default, which is effectively single-threaded). Lower it to 2 when several MediaPipe operators run at once. Ignored by Cpu and Gpu. |


## Outputs
| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |
| **MaskTexture** | T3.Core.DataTypes.Texture2D |
| **UpdateCount** | System.Int32 |
| **Confidence** | System.Single |
| **CategoryMask** | T3.Core.DataTypes.Texture2D |
| **ConfidenceMask** | T3.Core.DataTypes.Texture2D |
| **DebugTexture** | T3.Core.DataTypes.Texture2D |
