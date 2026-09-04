# FaceDetection

*in [Lib.io.video.mediapipe](README.md)*

Detects faces in an image using MediaPipe Face Detection.

This operator uses MediaPipe Face Detection task to detect faces in input image. It outputs detected faces as a buffer of points (bounding box corners and keypoints) and a dictionary of face data.

Inputs:
- InputTexture: The image to process.
- EnableDetection: Enables or disables face detection.
- ConfidenceThreshold: Minimum confidence score for a face to be detected.
- MaxFaces: Maximum number of faces to detect.
- ShowDetections: Whether to include bounding box points in the output buffer.
- ShowKeypoints: Whether to include keypoints (eyes, nose, mouth, ears) in the output buffer.
- DetectionSize: Size of points in the output buffer.
- DetectionColor: Color of bounding box points.
- KeypointColor: Color of keypoint points.
- UseCombinedAIOutput: Whether to generate unified AI texture outputs.
- Debug: Enables debug logging and debug texture output.
- CorrectAspectRatio: Whether to correct aspect ratio for non-square inputs.
- ZScale: Scale factor for Z coordinate of points.

Outputs:
- OutputTexture: The input texture (passed through).
- DetectionsBuffer: A structured buffer containing points for detected faces.
- FaceData: A dictionary containing detailed data for each detected face (bounding box coordinates, keypoint positions, confidence scores).
- FaceCount: The number of faces detected.
- UpdateCount: Increments each time a new frame is processed.
- DebugTexture: A debug visualization texture showing detected faces with bounding boxes and keypoints (only when Debug is enabled).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputTexture** (Texture2D) | The image to process. |
| **Enabled** (Boolean) | Enables or disables face detection. |
| **ConfidenceThreshold** (Single) | Minimum confidence score for a face to be detected. |
| **MaxFaces** (Int32) | Maximum number of faces to detect. |
| **ShowDetections** (Boolean) | Whether to include bounding box points in the output buffer. |
| **ShowKeypoints** (Boolean) | Whether to include keypoints (eyes, nose, mouth, ears) in the output buffer. |
| **DetectionSize** (Single) | Size of points in the output buffer. |
| **DetectionColor** (Vector4) | Color of bounding box points. |
| **KeypointColor** (Vector4) | Color of keypoint points. |
| **UseCombinedAiOutput** (Boolean) | Whether to generate unified AI texture outputs. |
| **Debug** (Boolean) | Enables debug logging and debug texture output. |
| **CorrectAspectRatio** (Boolean) | Whether to correct aspect ratio for non-square inputs. |
| **ZScale** (Single) | Scale factor for Z coordinate of points. |

## Outputs
| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |
| **PointBuffer** | T3.Core.DataTypes.BufferWithViews |
| **FaceData** | T3.Core.DataTypes.Dict`1[System.Single] |
| **FaceCount** | System.Int32 |
| **UpdateCount** | System.Int32 |
| **DebugTexture** | T3.Core.DataTypes.Texture2D |

