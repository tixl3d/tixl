# FaceLandmarkDetection

*in [Lib.io.video.mediapipe](README.md)*

Detects face landmarks in an image using MediaPipe Face Mesh.

This operator uses the MediaPipe Face Mesh task to detect faces and their landmarks (468 or 478 points per face) in the input image. It outputs the detected landmarks as a buffer of points and a dictionary of face data.

Inputs:
- InputTexture: The image to process.
- EnableDetection: Enables or disables face detection.
- MaxFaces: Maximum number of faces to detect.
- ShowLandmarks: Whether to include landmark points in the output buffer.
- LandmarkSize: Size of the landmark points in the output buffer.
- LandmarkColor: Color of the landmark points.
- LandmarkScale: Scale factor for the landmark positions (useful for 3D visualization).
- NormalizeLandmarks: Whether to normalize landmark coordinates to [0, 1] range.
- MinFaceDetectionConfidence: Minimum confidence score for face detection to be considered successful.
- MinFacePresenceConfidence: Minimum confidence score for face presence.
- MinTrackingConfidence: Minimum confidence score for face tracking.
- Debug: Enables debug logging.

Outputs:
- OutputTexture: The input texture (passed through).
- PointBuffer: A structured buffer containing points for detected face landmarks.
- FaceData: A dictionary containing detailed data for each detected face (landmark positions, blendshapes, transformation matrices).
- FaceCount: The number of faces detected.

Acceleration selects the inference delegate: Cpu, Xnnpack (multithreaded CPU, the fast default) or Gpu. Gpu needs a native build with the OpenGL delegate (Linux here) and otherwise falls back to Cpu; InferenceThreads sets the Xnnpack worker-thread count (0 = library default).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ZScale** (Single) | Z Scale. |
| **MinTrackingConfidence** (Single) | Minimum confidence score for face tracking. |
| **MinFaceDetectionConfidence** (Single) | Minimum confidence score for face detection to be considered successful. |
| **LandmarkSize** (Single) | Size of the landmark points in the output buffer. |
| **Debug** (Boolean) | Enables debug logging. |
| **MaxFaces** (Int32) | Maximum number of faces to detect. |
| **MinFacePresenceConfidence** (Single) | Minimum confidence score for face presence. |
| **LandmarkColor** (Vector4) | Color of the landmark points. |
| **UseCombinedAIOutput** (Boolean) | Use combined AI output. |
| **InputTexture** (Texture2D) | The image to process. |
| **Acceleration** (MediaPipeAcceleration) | Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere), Xnnpack = multithreaded CPU (the fast default), Gpu = OpenGL/EGL delegate (Linux; the Windows/macOS natives are CPU-only and fall back to Cpu). |
| **InferenceThreads** (Int32) | Xnnpack worker threads (default 4; 0 = library default, which is effectively single-threaded). Lower it to 2 when several MediaPipe operators run at once. Ignored by Cpu and Gpu. |
| **Enabled** (Boolean) | Enables or disables face detection. |
| **LandmarkScale** (Single) | Scale factor for the landmark positions (useful for 3D visualization). |
| **CorrectAspectRatio** (Boolean) | Correct aspect ratio. |
| **ShowLandmarks** (Boolean) | Whether to include landmark points in the output buffer. |
| **NormalizeLandmarks** (Boolean) | Whether to normalize landmark coordinates to [0, 1] range. |


## Outputs
| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |
| **FaceData** | T3.Core.DataTypes.Dict`1[System.Single] |
| **FaceCount** | System.Int32 |
| **DebugTexture** | T3.Core.DataTypes.Texture2D |
| **PointBuffer** | T3.Core.DataTypes.BufferWithViews |
| **Blendshapes** | T3.Core.DataTypes.Dict`1[System.Single] |
