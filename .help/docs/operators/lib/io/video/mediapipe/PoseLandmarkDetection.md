# PoseLandmarkDetection

*in [Lib.io.video.mediapipe](README.md)*

Detects pose landmarks in an image using MediaPipe Pose Landmark Detection.

This operator uses MediaPipe Pose Landmark Detection task to detect human poses and their landmarks (33 points per pose) in the input image. It outputs detected landmarks as a buffer of points and a dictionary of pose data.

Inputs:
- InputTexture: The image to process.
- Enabled: Enables or disables pose detection.
- MaxPoses: Maximum number of poses to detect.
- MinPoseDetectionConfidence: Minimum confidence score for pose detection to be considered successful.
- MinPosePresenceConfidence: Minimum confidence score for pose presence to be considered successful.
- MinTrackingConfidence: Minimum confidence score for pose tracking.
- Debug: Enables debug visualization with debug texture output.
- CorrectAspectRatio: Whether to correct aspect ratio for non-square inputs.
- ZScale: Scale factor for Z coordinate of points.

Outputs:
- OutputTexture: The input texture (passed through).
- DebugTexture: Debug visualization texture showing pose landmarks and connections when Debug is enabled.
- PoseCount: The number of poses detected.
- UpdateCount: Increments each time a new frame is processed.
- WorldLandmarksBuffer: A structured buffer containing world landmarks.
- PointBuffer: A structured buffer containing points for detected pose landmarks.
- SegmentationMask: A texture containing the segmentation mask.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputTexture** (Texture2D) | The image to process. |
| **Enabled** (Boolean) | Enables or disables pose detection. |
| **MaxPoses** (Int32) | Maximum number of poses to detect. |
| **MinPoseDetectionConfidence** (Single) | Minimum confidence score for pose detection to be considered successful. |
| **MinPosePresenceConfidence** (Single) | Minimum confidence score for pose presence to be considered successful. |
| **MinTrackingConfidence** (Single) | Minimum confidence score for pose tracking. |
| **Debug** (Boolean) | Enables debug visualization with debug texture output showing pose landmarks and connections. |
| **CorrectAspectRatio** (Boolean) | Whether to correct aspect ratio for non-square inputs. |
| **ZScale** (Single) | Scale factor for Z coordinate of points. |

## Outputs
| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |
| **DebugTexture** | T3.Core.DataTypes.Texture2D |
| **PoseCount** | System.Int32 |
| **UpdateCount** | System.Int32 |
| **WorldLandmarksBuffer** | T3.Core.DataTypes.BufferWithViews |
| **PointBuffer** | T3.Core.DataTypes.BufferWithViews |
| **SegmentationMask** | T3.Core.DataTypes.Texture2D |

