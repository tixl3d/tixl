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

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputTexture** (Texture2D) | The image to process. |
| **Enabled** (Boolean) | Enables or disables face detection. |
| **MaxFaces** (Int32) | Maximum number of faces to detect. |
| **ShowLandmarks** (Boolean) | Whether to include landmark points in the output buffer. |
| **LandmarkSize** (Single) | Size of the landmark points in the output buffer. |
| **LandmarkColor** (Vector4) | Color of the landmark points. |
| **LandmarkScale** (Single) | Scale factor for the landmark positions (useful for 3D visualization). |
| **NormalizeLandmarks** (Boolean) | Whether to normalize landmark coordinates to [0, 1] range. |
| **MinFaceDetectionConfidence** (Single) | Minimum confidence score for face detection to be considered successful. |
| **MinFacePresenceConfidence** (Single) | Minimum confidence score for face presence. |
| **MinTrackingConfidence** (Single) | Minimum confidence score for face tracking. |
| **Debug** (Boolean) | Enables debug logging. |
| **UseCombinedAIOutput** (Boolean) | Use combined AI output. |
| **CorrectAspectRatio** (Boolean) | Correct aspect ratio. |
| **ZScale** (Single) | Z Scale. |

## Outputs
| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |
| **FaceData** | T3.Core.DataTypes.Dict`1[System.Single] |
| **FaceCount** | System.Int32 |
| **DebugTexture** | T3.Core.DataTypes.Texture2D |
| **PointBuffer** | T3.Core.DataTypes.BufferWithViews |
| **Blendshapes** | T3.Core.DataTypes.Dict`1[System.Single] |

