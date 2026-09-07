# Lib.io.video

## Sub-namespaces

- [Lib.io.video.mediapipe](mediapipe/README.md)

## Operators

- [**CameraCalibrator**](CameraCalibrator.md) — Calibrates a camera to determine its intrinsic parameters (like focal length and lens distortion) and to remove distortion from the video feed.
- [**NdiInput**](NdiInput.md) — NDI live video input
- [**NdiOutput**](NdiOutput.md) — NDI live video output. R8G8B8A8_UNorm texture format or R8G8B8A8_Typeless is required for output. You can adjust the render format by using a ConvertFormat op or [RenderTarget] like in this example: [TorusMesh]->[DrawMesh]->[RenderTarget]->[SpoutOutput].
- [**PlayAudioClip**](PlayAudioClip.md) — This is a workaround to playback audio clips without syncing.
- [**PlayVideo**](PlayVideo.md) — Uses Windows Media Foundation to play a video file. To ensure seek precision while editing, it enforces seeking if timeline playback is paused. If timeline playback is running, it will only seek if the video playback drift exceeds the resync threshold. If this threshold is too small, playback will stutter. If it's excessively large, syncing might be off.
- [**PlayVideoClip**](PlayVideoClip.md) — Implementation to load and play video files with similar options like a [TimeClip]
- [**ScreenCapture**](ScreenCapture.md) — Loads and renders the content of the entire screen, similar to screen capturing software like OBS (Open Broadcaster Software).
- [**SpoutInput**](SpoutInput.md) — Spout live video input
- [**SpoutOutput**](SpoutOutput.md) — Spout live video output. We recommend using the R8G8B8A8_UNorm texture format for output. You can adjust the render format by using a [RenderTarget] like in this example: [TorusMesh]->[DrawMesh]->[RenderTarget]->[SpoutOutput].
- [**VideoClip**](VideoClip.md)
- [**VideoDeviceInput**](VideoDeviceInput.md) — Captures live video from a connected device like a webcam, capture card, or NDI source.
- [**VideoStreamInput**](VideoStreamInput.md) — Receives a video stream from a network source.

---

*Auto-generated from the operator library.*
