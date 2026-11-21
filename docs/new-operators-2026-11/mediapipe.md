# MediaPipe — on-device vision detections

All seven detection operators share the same shape: a video texture in, structured
result outputs out, inference running on worker threads with one shared texture
readback per frame. Every operator has an example composition next to it.

| Operator | Example | Detects |
| --- | --- | --- |
| `FaceDetection` | `FaceDetectionExample` | Faces with score and keypoints |
| `FaceLandmarkDetection` | `FaceLandmarkDetectionExample` | 478-point face mesh |
| `PoseLandmarkDetection` | `PoseLandmarkDetectionExample` | 33-point body skeleton |
| `HandLandmarkDetection` | `HandLandmarkDetectionExample` | 21-point hand skeleton, two hands |
| `GestureRecognition` | `GestureRecognitionExample` | Named gestures from the hand landmarks |
| `ObjectDetection` | `ObjectDetectionExample` | Bounding boxes with class labels |
| `ImageSegmentation` | `ImageSegmentationExample` | Per-pixel class masks |
| — | `MediaPipeExample` | All detectors side by side on one feed |

Inference threads and the readback cadence are shared per editor process
(`InferenceThreads` defaults to 4); the ops draw their results through the standard
point/line draw operators.
