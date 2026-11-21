# HandLandmarkDetection

*in [Lib.io.video.mediapipe](README.md)*

Detects hand landmarks in an image using MediaPipe Hand Landmark Detection.

This operator uses MediaPipe Hand Landmark Detection task to detect hands and their landmarks (21 points per hand) in the input image. It outputs detected landmarks as a buffer of points and a dictionary of hand data.

Inputs:
- InputTexture: The image to process.
- Enabled: Enables or disables hand detection.
- MaxHands: Maximum number of hands to detect.
- MinHandDetectionConfidence: Minimum confidence score for hand detection to be considered successful.
- MinHandPresenceConfidence: Minimum confidence score for hand presence to be considered successful.
- MinTrackingConfidence: Minimum confidence score for hand tracking.
- Debug: Enables debug visualization with debug texture output.
- CorrectAspectRatio: Whether to correct aspect ratio for non-square inputs.
- ZScale: Scale factor for Z coordinate of points.

Outputs:
- OutputTexture: The input texture (passed through).
- DebugTexture: Debug visualization texture showing hand landmarks and connections when Debug is enabled.
- HandCount: The number of hands detected.
- UpdateCount: Increments each time a new frame is processed.
- WorldLandmarksBuffer: A structured buffer containing world landmarks.
- PointBuffer: A structured buffer containing points for detected hand landmarks.

Acceleration selects the inference delegate: Cpu, Xnnpack (multithreaded CPU, the fast default) or Gpu. Gpu needs a native build with the OpenGL delegate (Linux here) and otherwise falls back to Cpu; InferenceThreads sets the Xnnpack worker-thread count (0 = library default).

ConstantSize rescales each detected hand around its wrist so its on-screen size stays constant as it moves toward or away from the camera; HandSize is the target wrist-to-middle-knuckle distance in that mode.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **MinTrackingConfidence** (Single) | Minimum confidence score for hand tracking. |
| **InputTexture** (Texture2D) | The image to process. |
| **Enabled** (Boolean) | Enables or disables hand detection. |
| **MaxHands** (Int32) | Maximum number of hands to detect. |
| **MinHandDetectionConfidence** (Single) | Minimum confidence score for hand detection to be considered successful. |
| **MinHandPresenceConfidence** (Single) | Minimum confidence score for hand presence to be considered successful. |
| **Acceleration** (MediaPipeAcceleration) | Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere), Xnnpack = multithreaded CPU (the fast default), Gpu = OpenGL/EGL delegate (Linux; the Windows/macOS natives are CPU-only and fall back to Cpu). |
| **InferenceThreads** (Int32) | Xnnpack worker threads (default 4; 0 = library default, which is effectively single-threaded). Lower it to 2 when several MediaPipe operators run at once. Ignored by Cpu and Gpu. |
| **ConstantSize** (Boolean) | Rescales each hand's landmark cloud around its wrist so its size no longer depends on the distance to the camera. |
| **HandSize** (Single) | Target wrist-to-middle-knuckle distance (in centered, +/-0.5 image units) when ConstantSize is on. |
| **Debug** (Boolean) | Enables debug visualization with debug texture output showing hand landmarks and connections. |
| **CorrectAspectRatio** (Boolean) | Whether to correct aspect ratio for non-square inputs. |
| **ZScale** (Single) | Scale factor for Z coordinate of points. |


## Outputs
| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |
| **DebugTexture** | T3.Core.DataTypes.Texture2D |
| **HandCount** | System.Int32 |
| **UpdateCount** | System.Int32 |
| **WorldLandmarksBuffer** | T3.Core.DataTypes.BufferWithViews |
| **PointBuffer** | T3.Core.DataTypes.BufferWithViews |
