# ViscaCamera

*in [Lib.io.ptz](README.md)*

Controls a PTZ camera using the VISCA over IP protocol.

This operator sends UDP packets to control Pan, Tilt, and Zoom. It supports absolute positioning.

AKA: PTZ, Sony VISCA, Network Camera Control

Tips:
* Ensure the camera is configured to accept VISCA over IP (often port 52381).
* Pan and Tilt inputs are normalized (-1..1). Adjust 'Pan Range' and 'Tilt Range' to match your specific camera model's max steps (e.g., Sony SRG series often use 0x1800 for Pan).
* Zoom is normalized (0..1).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **TextureInput** (Texture2D) | — |
| **LocalIpAddress** (String) | The local network interface to use for sending VISCA commands. Useful if you have multiple network adapters. |
| **Address** (String) | The IP address of the camera (e.g., 192.168.1.100). |
| **Port** (Int32) | The UDP port used for VISCA over IP communication. Default is usually 52381. |
| **Connect** (Boolean) | Toggles the connection state. When enabled, it resets the sequence number and prepares for communication. |
| **Pan** (Single) | Target horizontal rotation, normalized between -1 (left) and 1 (right). |
| **Tilt** (Single) | Target vertical rotation, normalized between -1 (down) and 1 (up). |
| **Zoom** (Single) | Target zoom level, normalized between 0 (wide) and 1 (tele). |
| **Move** (Boolean) | When enabled, sends the absolute move commands to the camera based on the Pan, Tilt, and Zoom inputs. |
| **PanRange** (Int32) | The maximum pan step value defined by the camera's VISCA implementation (e.g., 2448 for +/- 170 degrees). |
| **TiltRange** (Int32) | The maximum tilt step value defined by the camera's VISCA implementation (e.g., 1296 for +/- 90 degrees). |
| **PrintToLog** (Boolean) | If enabled, logs connection status and errors to the application console. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |
| **IsConnected** | System.Boolean |
| **CurrentPtz** | System.Numerics.Vector3 |

