# WebcamInput

*in [Lib.io.video](README.md)*

Captures live video from a connected device like a webcam or capture card.

Devices are opened through FFmpeg - DirectShow on Windows, V4L2 on Linux, AVFoundation on macOS. The device, resolution, and frame rate can be selected, and the capture runs on its own thread and automatically retries when a device is unplugged and returns. Basic image manipulation like flipping, rotation, scaling and repositioning is done in software.

The `Control1`..`Control4` pairs reach any control the device actually exposes: each pair is a control name plus its value, applied to the camera whenever it changes. By default the values are in device units; `NormalizedControlValues` maps them to 0..1 over each control's range instead. The `ControlsCatalog` output lists everything the device reports - kind, range, step, default, current value and auto/inactive state.

Tips:
- To save system resources, enable `DeactivateWhenNotShowing`. This will pause the capture when the operator is not being rendered.
- The `Status` output provides useful information for troubleshooting connection issues.
- `OpenSettings` opens a camera settings window; `Control1`..`Control4` drive any camera control (pan, tilt, zoom, focus...) from the graph.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **CustomFps** (Int32) | Manually specify a frame rate. This will only work if the device and its driver support the requested FPS. Default is 0 (fastest rate of the selected resolution). |
| **NormalizedControlValues** (Boolean) | If enabled, Control1Value..Control4Value range from 0 to 1 over each control's range as the camera reports it. If disabled, they are in the camera's own units, the numbers the settings window shows. |
| **Control4Value** (Single) | Value for Control4. Sent to the camera whenever it changes; with NormalizedControlValues it is 0..1 over the control's range. |
| **DeactivateWhenNotShowing** (Boolean) | If enabled, capture stops after a few seconds without this operator being evaluated — that is, while its output is not being rendered — releasing the camera for other applications and saving the CPU spent converting frames. Capture resumes within a moment of the operator being rendered again. |
| **Active** (Boolean) | Enables or disables the video capture. |
| **Control1** (String) | A camera control to drive from the graph, such as Pan, Tilt, Zoom or Focus. The dropdown lists what the selected camera supports. |
| **FlipVertically** (Boolean) | Mirrors the incoming video image left to right. The name is kept for existing projects. |
| **ApplyRotationData** (Single) | Applies a rotation to the video image in degrees. This is used for correcting orientation. |
| **Control3Value** (Single) | Value for Control3. Sent to the camera whenever it changes; with NormalizedControlValues it is 0..1 over the control's range. |
| **ResolutionFpsType** (Int32) | Selects how resolution and FPS are determined. 'Custom' allows manual input, otherwise the largest mode up to 1080p is used. |
| **CameraSettings** (String) | Camera values committed with OK or Apply in the settings window (see OpenSettings), stored with the project and applied again whenever the camera opens. Edit them in the window rather than here. |
| **Control2Value** (Single) | Value for Control2. Sent to the camera whenever it changes; with NormalizedControlValues it is 0..1 over the control's range. |
| **Reconnect** (Boolean) | A trigger to force a reconnection to the video device, which can be useful if the device becomes unresponsive or if settings have been changed externally. |
| **Control1Value** (Single) | Value for Control1. Sent to the camera whenever it changes; with NormalizedControlValues it is 0..1 over the control's range. |
| **Scale** (Vector2) | Scales the video image. Part of the `Reposition` transform. Default is 1,1 (no scaling). |
| **OpenSettings** (Boolean) | Opens the camera settings window: every control the camera supports (brightness, contrast, white balance, pan, tilt, zoom, focus...) with sliders and Auto switches, the same on Windows, Linux and macOS. Values committed with OK or Apply are stored in CameraSettings. |
| **Control4** (String) | A camera control to drive from the graph, such as Pan, Tilt, Zoom or Focus. The dropdown lists what the selected camera supports. |
| **FlipHorizontally** (Boolean) | Flips the incoming video image upside down. The name is kept for existing projects. |
| **CustomResolution** (Int2) | Manually specify a resolution (width and height). A size the device does not offer is scaled, which costs CPU time. Default is 0,0 (largest mode up to 1080p). |
| **Control3** (String) | A camera control to drive from the graph, such as Pan, Tilt, Zoom or Focus. The dropdown lists what the selected camera supports. |
| **Reposition** (Vector2) | Applies a 2D translation (X,Y) to the video image. Part of the transformation. |
| **InputDeviceName** (String) | The name of the video capture device to use. The dropdown lists all devices detected on the system. |
| **Control2** (String) | A camera control to drive from the graph, such as Pan, Tilt, Zoom or Focus. The dropdown lists what the selected camera supports. |


## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |
| **Resolution** | T3.Core.DataTypes.Vector.Int2 |
| **Status** | System.String |
| **UpdateCount** | System.Int32 |
