# FreeDOutput

*in [Lib.io.freed](README.md)*

Sends camera tracking data over UDP using the FreeD protocol. This is essential for virtual production, augmented reality, and broadcasting, allowing real-time integration with systems like Unreal Engine, Vizrt, or Aximmetry.

Also see: [FreeDInput]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Connect** (Boolean) | Enable to open the UDP socket and begin sending data. Disable to close the connection. |
| **LocalIpAddress** (String) | The local network adapter (IP address) to send from. '0.0.0.0' lets the OS decide, which is usually sufficient. |
| **TargetIpAddress** (String) | The IP address of the destination device. Leave empty to broadcast to all devices on the local network (255.255.255.255). |
| **TargetPort** (Int32) | The UDP port on the destination device. This must match the port the receiver is listening on. |
| **Rotation** (Vector3) | The camera's rotation as Euler angles (Pitch, Yaw, Roll) in degrees. |
| **Position** (Vector3) | The camera's position in 3D space (X, Y, Z). |
| **CameraId** (Int32) | A unique identifier (0-255) for the camera. The receiver uses this ID to distinguish between multiple camera sources. |
| **Focus** (Int32) | Lens focus distance value. This is a 24-bit integer (0 - 16,777,215) from the lens encoder. |
| **Zoom** (Int32) | Lens zoom value (focal length). This is a 24-bit integer (0 - 16,777,215) from the lens encoder. |
| **User** (Int32) | A 16-bit integer (0 - 65,535) for sending custom user-defined data, if supported by the camera system. |
| **SendOnChange** (Boolean) | If enabled, a FreeD packet is sent automatically on every frame. If disabled, packets are only sent when 'SendTrigger' is activated. |
| **SendTrigger** (Boolean) | Manually sends a single FreeD packet when triggered. Useful when 'SendOnChange' is disabled. |
| **PrintToLog** (Boolean) | Enable to print detailed activity and error messages to the T3 log console for debugging. |

## Outputs
| Name | Type |
|---|---|
| **Command** | T3.Core.DataTypes.Command |
| **IsConnected** | System.Boolean |

