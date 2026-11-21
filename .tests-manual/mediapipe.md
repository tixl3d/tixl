---
id: mediapipe
title: MediaPipe Operators
scope: operators
tags: [user]
added: 2026-11-20
added-in-version: 4.4
prerequisites:
  - A camera is connected and not in use by another program.
  - A person's face, hands, pose or objects are visible to the camera as each step requires.
related-help:
  - ../.help/docs/operators/lib/io/video/mediapipe/README.md
---

Checks that the seven MediaPipe operators detect, share one camera cleanly and expose their inference settings.

## Step: Single operator detects

**Action:**
Add a `[FaceDetection]`, wire a `[WebcamInput]` into `InputTexture`, enable `Enabled` and pin the operator to the output.

**Expected:**
- A debug view appears with a bounding box and the 6 keypoints per detected face.
- `FaceCount` counts the faces in view; `FaceData` fills the dictionary output.
- With nothing face-like in view, outputs hold empty values instead of the last detection.

## Step: Acceleration and threads

**Action:**
Switch `Acceleration` between `Cpu` and `Xnnpack` and lower `InferenceThreads` to 2.

**Expected:**
- The operator re-initializes briefly after each change and keeps detecting afterwards.
- The console shows no persistent warnings; the editor stays responsive during init.

## Step: Seven operators share one camera

**Action:**
Open the `[MediaPipeExample]` and let it run for a minute.

**Expected:**
- Only the operator selected by `Index` runs on the camera image each frame.
- The editor keeps playing while models load; each model initializes within its retry window and reports through `Status`.
- Turning the camera away from any subject clears that operator's outputs instead of freezing them.

## Step: Gesture recognition

**Action:**
Show an open palm, then a fist, to `[GestureRecognition]`.

**Expected:**
- The gesture name appears in the output; the score column stays stable while the same gesture is held.
- With no hand in view, `GestureName` clears.

## Step: Segmentation mask clears

**Action:**
Point `[ImageSegmentation]` at the camera, then move out of frame.

**Expected:**
- The mask follows the person while in view; `MaskTexture` tints only the selected category.
- When nobody is in view, `MaskTexture`/`CategoryMask`/`ConfidenceMask` outputs clear instead of holding the last mask.
