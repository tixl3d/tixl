# GestureRecognition

*in [Lib.io.video.mediapipe](README.md)*

Recognizes hand gestures in an image on top of MediaPipe hand landmark detection.

This operator uses the MediaPipe Hand Landmark task to detect hands and derives a basic gesture from the landmark poses in the input image. It outputs the recognized gesture name and a fixed confidence value per gesture type (not a classifier score).

Inputs:
- InputTexture: The image to process.
- Enabled: Enables or disables gesture recognition.
- MaxHands: Maximum number of hands to detect.
- MinHandDetectionConfidence: Minimum confidence score for hand detection to be considered successful.
- MinHandPresenceConfidence: Minimum confidence score for hand presence to be considered successful.
- UseCombinedAIOutput: Whether to generate unified AI texture outputs.
- Debug: Enables debug visualization with debug texture output.
- CategoryAllowlist: Reserved for a MediaPipe gesture classifier; not applied by the built-in gesture rules.
- CategoryDenylist: Reserved for a MediaPipe gesture classifier; not applied by the built-in gesture rules.
- CorrectAspectRatio: Whether to correct aspect ratio for non-square inputs.
- ZScale: Scale factor for Z coordinate of points.

Outputs:
- OutputTexture: The input texture (passed through).
- DebugTexture: Debug visualization texture showing hand landmarks and connections when Debug is enabled.
- RecognizedGestures: A list of recognized gesture names (None, Fist, Open Palm, Point, Peace, Thumbs Up).
- PrimaryGesture: The name of the primary recognized gesture (None, Fist, Open Palm, Point, Peace, Thumbs Up).
- Confidence: A fixed value per gesture type for the recognized gesture.
- HandCount: The number of hands detected.
- UpdateCount: Increments each time a new frame is processed.
- Handedness: A list of handedness (Left/Right) for detected hands.
- WorldLandmarksBuffer: A structured buffer containing points for detected hand landmarks in world coordinates.
- PointBuffer: A structured buffer containing points for visualization.

Acceleration selects the inference delegate: Cpu, Xnnpack (multithreaded CPU, the fast default) or Gpu. Gpu needs a native build with the OpenGL delegate (Linux here) and otherwise falls back to Cpu; InferenceThreads sets the Xnnpack worker-thread count (0 = library default).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **CategoryDenylist** (String) | Comma-separated list of category names to deny (e.g. "Thumb_Up"). |
| **CorrectAspectRatio** (Boolean) | Whether to correct aspect ratio for non-square inputs. |
| **Acceleration** (MediaPipeAcceleration) | Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere), Xnnpack = multithreaded CPU (the fast default), Gpu = OpenGL/EGL delegate (Linux; the Windows/macOS natives are CPU-only and fall back to Cpu). |
| **InferenceThreads** (Int32) | Xnnpack worker threads (default 4; 0 = library default, which is effectively single-threaded). Lower it to 2 when several MediaPipe operators run at once. Ignored by Cpu and Gpu. |
| **Debug** (Boolean) | Enables debug visualization with debug texture output showing hand landmarks and connections. |
| **InputTexture** (Texture2D) | The image to process. |
| **ZScale** (Single) | Scale factor for Z coordinate of points. |
| **Enabled** (Boolean) | Enables or disables gesture recognition. |
| **MaxHands** (Int32) | Maximum number of hands to detect. |
| **MinHandDetectionConfidence** (Single) | Minimum confidence score for hand detection to be considered successful. |
| **MinHandPresenceConfidence** (Single) | Minimum confidence score for hand presence to be considered successful. |
| **UseCombinedAIOutput** (Boolean) | Whether to generate unified AI texture outputs. |
| **CategoryAllowlist** (String) | Comma-separated list of category names to allow (e.g. "Thumb_Up"). |


## Outputs
| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |
| **DebugTexture** | T3.Core.DataTypes.Texture2D |
| **RecognizedGestures** | System.Collections.Generic.List`1[System.String] |
| **PrimaryGesture** | System.String |
| **Confidence** | System.Single |
| **HandCount** | System.Int32 |
| **UpdateCount** | System.Int32 |
| **Handedness** | System.Collections.Generic.List`1[System.String] |
| **WorldLandmarksBuffer** | T3.Core.DataTypes.BufferWithViews |
| **PointBuffer** | T3.Core.DataTypes.BufferWithViews |
