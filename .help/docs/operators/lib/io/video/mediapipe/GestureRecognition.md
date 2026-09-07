# GestureRecognition

*in [Lib.io.video.mediapipe](README.md)*

Recognizes hand gestures in an image using MediaPipe Gesture Recognition.

This operator uses MediaPipe Gesture Recognition task to detect hands and recognize gestures (e.g., "Thumb_Up", "Victory", "Closed_Fist") in the input image. It outputs the recognized gesture name and confidence score.

Inputs:
- InputTexture: The image to process.
- Enabled: Enables or disables gesture recognition.
- MaxHands: Maximum number of hands to detect.
- MinHandDetectionConfidence: Minimum confidence score for hand detection to be considered successful.
- MinHandPresenceConfidence: Minimum confidence score for hand presence to be considered successful.
- UseCombinedAIOutput: Whether to generate unified AI texture outputs.
- Debug: Enables debug visualization with debug texture output.
- CategoryAllowlist: Comma-separated list of category names to allow (e.g. "Thumb_Up").
- CategoryDenylist: Comma-separated list of category names to deny (e.g. "Thumb_Up").
- CorrectAspectRatio: Whether to correct aspect ratio for non-square inputs.
- ZScale: Scale factor for Z coordinate of points.

Outputs:
- OutputTexture: The input texture (passed through).
- DebugTexture: Debug visualization texture showing hand landmarks and connections when Debug is enabled.
- RecognizedGestures: A list of recognized gesture names (e.g., "Thumb_Up").
- PrimaryGesture: The name of the primary recognized gesture.
- Confidence: The confidence score of the recognized gesture.
- HandCount: The number of hands detected.
- UpdateCount: Increments each time a new frame is processed.
- Handedness: A list of handedness (Left/Right) for detected hands.
- WorldLandmarksBuffer: A structured buffer containing points for detected hand landmarks in world coordinates.
- PointBuffer: A structured buffer containing points for visualization.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputTexture** (Texture2D) | The image to process. |
| **Enabled** (Boolean) | Enables or disables gesture recognition. |
| **MaxHands** (Int32) | Maximum number of hands to detect. |
| **MinHandDetectionConfidence** (Single) | Minimum confidence score for hand detection to be considered successful. |
| **MinHandPresenceConfidence** (Single) | Minimum confidence score for hand presence to be considered successful. |
| **UseCombinedAIOutput** (Boolean) | Whether to generate unified AI texture outputs. |
| **Debug** (Boolean) | Enables debug visualization with debug texture output showing hand landmarks and connections. |
| **CategoryAllowlist** (String) | Comma-separated list of category names to allow (e.g. "Thumb_Up"). |
| **CategoryDenylist** (String) | Comma-separated list of category names to deny (e.g. "Thumb_Up"). |
| **CorrectAspectRatio** (Boolean) | Whether to correct aspect ratio for non-square inputs. |
| **ZScale** (Single) | Scale factor for Z coordinate of points. |

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

