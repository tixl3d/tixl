# ObjectDetection

*in [Lib.io.video.mediapipe](README.md)*

Detects objects in an image using MediaPipe Object Detection.

This operator uses MediaPipe Object Detection task to detect objects in the input image. It outputs detected objects as a buffer of points (bounding box corners) and a dictionary of object data.

Inputs:
- InputTexture: The image to process.
- Enabled: Enables or disables object detection.
- MaxObjects: Maximum number of objects to detect.
- MinDetectionConfidence: Minimum confidence score for an object to be detected.
- Model: Selects the object detection model to use.
- Debug: Enables debug visualization with debug texture output.
- CategoryAllowlist: Comma-separated list of category names to allow (e.g. "person, cup").
- CategoryDenylist: Comma-separated list of category names to deny (e.g. "person, cup").
- CorrectAspectRatio: Whether to correct aspect ratio for non-square inputs.
- ZScale: Scale factor for Z coordinate of points.

Outputs:
- OutputTexture: The input texture (passed through).
- DebugTexture: Debug visualization texture showing bounding boxes and labels when Debug is enabled.
- ObjectCount: The number of objects detected.
- UpdateCount: Increments each time a new frame is processed.
- PointBuffer: A structured buffer containing points for detected objects (bounding box corners).
- ObjectData: A dictionary containing detailed data for each detected object (bounding box coordinates, category name, confidence score).
- ActiveCategories: A list of currently active detection categories.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputTexture** (Texture2D) | The image to process. |
| **Enabled** (Boolean) | Enables or disables object detection. |
| **MaxObjects** (Int32) | Maximum number of objects to detect. |
| **MinDetectionConfidence** (Single) | Minimum confidence score for an object to be detected. |
| **Model** (Int32) | Selects the object detection model to use. |
| **Debug** (Boolean) | Enables debug visualization with debug texture output showing bounding boxes and labels. |
| **CategoryAllowlist** (String) | Comma-separated list of category names to allow (e.g. "person, cup"). |
| **CategoryDenylist** (String) | Comma-separated list of category names to deny (e.g. "person, cup"). |
| **CorrectAspectRatio** (Boolean) | Whether to correct aspect ratio for non-square inputs. |
| **ZScale** (Single) | Scale factor for Z coordinate of points. |

## Outputs
| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |
| **DebugTexture** | T3.Core.DataTypes.Texture2D |
| **ObjectCount** | System.Int32 |
| **UpdateCount** | System.Int32 |
| **PointBuffer** | T3.Core.DataTypes.BufferWithViews |
| **ObjectData** | T3.Core.DataTypes.Dict`1[System.Single] |
| **ActiveCategories** | System.Collections.Generic.List`1[System.String] |

