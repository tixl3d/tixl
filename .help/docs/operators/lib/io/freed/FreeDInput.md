# FreeDInput

*in [Lib.io.freed](README.md)*

Receives camera tracking data over UDP using the FreeD protocol. This is essential for virtual production, augmented reality, and broadcasting, allowing real-time integration with systems like Unreal Engine, Vizrt, or Aximmetry.

Also see: [FreeDOutput]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Listen** (Boolean) | Enable to open the UDP socket and begin listening for data. Disable to close the connection. |
| **LocalIpAddress** (String) | The local network adapter (IP address) to listen on. '0.0.0.0 (Any)' listens on all available adapters, which is usually what you want. |
| **Port** (Int32) | The UDP port to listen on. This must match the port the sender is broadcasting to. |
| **CameraId** (Int32) | Selects a specific camera ID to output on the single-camera outputs (CameraPos, CameraRot, etc.). If set to -1, it automatically selects the camera with the lowest ID. |
| **PrintToLog** (Boolean) | Enable to print detailed activity and error messages to the T3 log console for debugging. |

## Outputs
| Name | Type |
|---|---|
| **CamerasAsBuffer** | T3.Core.DataTypes.BufferWithViews |
| **CameraDataAsDict** | T3.Core.DataTypes.Dict`1[System.Single] |
| **IsListening** | System.Boolean |
| **CameraPos** | System.Numerics.Vector3 |
| **CameraRot** | System.Numerics.Vector3 |
| **Zoom** | System.Single |
| **Focus** | System.Single |
| **User** | System.Single |

