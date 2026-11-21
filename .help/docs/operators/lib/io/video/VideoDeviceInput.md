# VideoDeviceInput

*in [Lib.io.video](README.md)*

Captures live video from a connected device like a webcam or capture card, with the basic camera hardware configs built in: Pan, Tilt, Zoom, Focus, Exposure and Brightness are applied to the camera automatically whenever their input changes — several settings at once, and each can be patched/animated.

Beyond the fixed config inputs, a generic control surface reaches every control the device actually exposes — the replacement for the old DirectShow capture-filter dialog: `ControlName` offers the discovered controls as a dropdown, `ControlValue` carries the value in device units (for menu controls it picks one of the item values its dropdown lists), `ControlAuto` is a strict two-way switch (true = engage the control's auto mode, value ignored; false = apply the value as manual, which disengages auto), and `ApplyControl` sends the combination to the camera. The `ControlsCatalog` output lists everything the device reports — kind, range, step, default, current value and auto/inactive state. On platforms without a camera-control backend, both report controls as unavailable.

Devices are opened through FFmpeg — DirectShow on Windows, V4L2 on Linux, AVFoundation on macOS. You can select the device, resolution, and frame rate; the capture runs on its own thread and automatically retries when a device is unplugged and returns. Basic image manipulation like flipping, rotation, scaling and repositioning is done in software.

Tips:
- To save system resources, enable `DeactivateWhenNotShowing`. This will pause the capture when the operator is not being rendered.
- The config inputs only touch the camera when their value changes, and only if the camera actually implements the control. Values are in device units (Zoom 100 = 1x, Pan/Tilt in 1/64°); `Status` shows the supported range after an apply. All control traffic runs on a background worker, so success notes, clamping and unsupported warnings land one evaluation after the change, and an animated input coalesces into one pending write.
- The resolution and frame-rate dropdowns list the combinations the device reports (on macOS the device doesn't expose them, so only defaults are offered); with `ResolutionFpsType` set to `Custom`, any value can be typed — the driver negotiates the closest match and the image is resized in software when the device can't deliver the requested size.
- On macOS the camera is gated by privacy: if the editor is denied camera access, no devices appear — allow it under System Settings → Privacy & Security → Camera, then trigger `Reconnect`.
- A `Reconnect` trigger also rescans the device list, so cameras plugged in after startup become selectable, and refreshes the control catalog of the (possibly replaced) camera.
- The `Status` output provides useful information for troubleshooting connection issues.

AKA: webcam, camera, capture card, ptz, pan, tilt, zoom, video input

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **ControlValue** (Single) | Value in device units for the selected control (see `ControlsCatalog` for its range and current value). For menu controls this is the selected item's value — the dropdown lists the items as 'value — label'; for non-menu controls the typed value is used as-is. |
| **CustomFps** (Int32) | Manually specify a frame rate. This will only work if the device and its driver support the requested FPS. Default is 0 (device default). |
| **ControlAuto** (Boolean) | Strict two-way switch: true engages the selected control's auto mode (`ControlValue` is ignored); false, the default, applies `ControlValue` as a manual value — which disengages the auto mode on every platform. There is deliberately no 'leave auto untouched' option. |
| **DeactivateWhenNotShowing** (Boolean) | If enabled, the device is automatically deactivated when its output is not being rendered, which can save significant system resources. |
| **Active** (Boolean) | Enables or disables the video capture. |
| **Tilt** (Single) | Camera tilt in device units (UVC: 1/64°). Applied when the value changes; ignored if the camera has no tilt. |
| **FlipVertically** (Boolean) | Flips the incoming video image vertically. |
| **ApplyRotationData** (Single) | Applies a rotation to the video image in degrees. This is used for correcting orientation. |
| **ResolutionFpsType** (Int32) | Selects how resolution and FPS are determined. 'Custom' allows manual input, otherwise device defaults are used. |
| **Reconnect** (Boolean) | A trigger to force a reconnection to the video device, which can be useful if the device becomes unresponsive or if settings have been changed externally. |
| **ControlName** (String) | The device control to drive, picked from everything the camera actually reports — the dropdown lists the same controls as the `ControlsCatalog` output. Send it with `ApplyControl`. |
| **Zoom** (Single) | Camera zoom in device units (UVC: 100 = 1x). Applied when the value changes; ignored if the camera has no zoom. |
| **ApplyControl** (Boolean) | Trigger that sends the ControlName/ControlValue/ControlAuto combination to the camera and self-clears. Control traffic runs on a background worker — `Status` reports the result (clamping, unsupported controls) one evaluation later. |
| **Scale** (Vector2) | Scales the video image. Part of the `Reposition` transform. Default is 1,1 (no scaling). |
| **Exposure** (Single) | Camera exposure in device units (UVC absolute exposure: 0.1 ms). Applied when the value changes; ignored if the camera has no manual exposure. |
| **OpenSettings** (Boolean) | A trigger to open the video device's native driver settings dialog, if available. |
| **FlipHorizontally** (Boolean) | Flips the incoming video image horizontally. |
| **Focus** (Single) | Camera focus in device units (absolute). Applied when the value changes; ignored if the camera has no manual focus. |
| **CustomResolution** (Int2) | Manually specify a resolution (width and height). This will only work if the device and its driver support the requested resolution. Default is 0,0 (device default). |
| **Brightness** (Single) | Camera brightness in device units. Applied when the value changes; ignored if the camera has no brightness control. |
| **Pan** (Single) | Camera pan in device units (UVC: 1/64°). Applied when the value changes; ignored if the camera has no pan. |
| **Reposition** (Vector2) | Applies a 2D translation (X,Y) to the video image. Part of the transformation. |
| **InputDeviceName** (String) | The name of the video capture device to use. The dropdown lists all devices detected on the system. |

## Outputs

| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |
| **Resolution** | T3.Core.DataTypes.Vector.Int2 |
| **ControlsCatalog** | System.String |
| **UpdateCount** | System.Int32 |


