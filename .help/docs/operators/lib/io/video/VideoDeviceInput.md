# VideoDeviceInput

*in [Lib.io.video](README.md)*

Captures live video from a connected device like a webcam, capture card, or NDI source.

This operator provides access to video devices available on your system. You can select the device, resolution, and frame rate. It also allows for basic image manipulation like flipping and repositioning.

Tips:
- To save system resources, enable `DeactivateWhenNotShowing`. This will pause the capture when the operator is not being rendered.
- Use `OpenSettings` to access the device-specific configuration dialog for more advanced options like exposure and color balance.
- The `Status` output provides useful information for troubleshooting connection issues.

AKA: webcam, camera, capture card, ndi, directshow, video input

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Active** (Boolean) | Enables or disables the video capture. |
| **ApplyRotationData** (Single) | Applies a rotation to the video image in degrees. This is used for correcting orientation. |
| **CustomFps** (Int32) | Manually specify a frame rate. This will only work if the device and its driver support the requested FPS. Default is 0 (device default). |
| **CustomResolution** (Int2) | Manually specify a resolution (width and height). This will only work if the device and its driver support the requested resolution. Default is 0,0 (device default). |
| **DeactivateWhenNotShowing** (Boolean) | If enabled, the device is automatically deactivated when its output is not being rendered, which can save significant system resources. |
| **FlipHorizontally** (Boolean) | Flips the incoming video image horizontally. |
| **FlipVertically** (Boolean) | Flips the incoming video image vertically. |
| **InputDeviceName** (String) | The name of the video capture device to use. The dropdown lists all devices detected on the system. |
| **OpenSettings** (Boolean) | A trigger to open the video device's native driver settings dialog, if available. |
| **Reconnect** (Boolean) | A trigger to force a reconnection to the video device, which can be useful if the device becomes unresponsive or if settings have been changed externally. |
| **Reposition** (Vector2) | Applies a 2D translation (X,Y) to the video image. Part of the transformation. |
| **ResolutionFpsType** (Int32) | Selects how resolution and FPS are determined. 'Custom' allows manual input, otherwise device defaults are used. |
| **Scale** (Vector2) | Scales the video image. Part of the `Reposition` transform. Default is 1,1 (no scaling). |

## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |
| **Resolution** | T3.Core.DataTypes.Vector.Int2 |
| **Status** | System.String |
| **UpdateCount** | System.Int32 |

