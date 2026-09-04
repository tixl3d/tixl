# OnvifCamera

*in [Lib.io.ptz](README.md)*

Connects to an ONVIF-compliant IP camera to retrieve video texture and control PTZ.

This operator handles the discovery, connection, and control of network cameras supporting the ONVIF standard. It uses FFmpeg to capture the video stream (RTSP) and SOAP requests for PTZ control.

The video stream is output as a Texture2D.
PTZ controls are normalized (Pan/Tilt -1..1, Zoom 0..1).

AKA: IPCamera, RTSP, NetworkCamera, SecurityCamera, PTZ

Tips:
* Use the 'Discover' toggle to find cameras on your local network.
* Ensure the 'Local IP Address' is set to the network adapter connected to the cameras.
* PTZ movements are damped/smoothed.
* If the stream URL is not automatically resolved, you can enter a direct RTSP URL in the Address field.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **LocalIpAddress** (String) | The local network interface to use for discovery and connection. Select the adapter connected to the camera network. |
| **Discover** (Boolean) | Starts scanning the local network (via WS-Discovery) for ONVIF devices. Found devices will appear in the Address dropdown. |
| **Address** (String) | The IP address or RTSP URL of the camera. If 'Discover' is used, you can select found cameras from the dropdown. |
| **Username** (String) | The username for camera authentication (ONVIF and RTSP). |
| **Password** (String) | The password for camera authentication. |
| **Connect** (Boolean) | Initiates the connection to the camera stream and PTZ services. |
| **Move** (Boolean) | When enabled, sends AbsoluteMove commands to the camera based on Pan, Tilt, and Zoom inputs. |
| **Pan** (Single) | Target horizontal rotation (normalized -1..1). |
| **Tilt** (Single) | Target vertical rotation (normalized -1..1). |
| **Zoom** (Single) | Target zoom level (normalized 0..1). |
| **PrintToLog** (Boolean) | Enables detailed logging to the T3 console for debugging connection or PTZ issues. |

## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |
| **IsConnected** | System.Boolean |
| **CurrentPtz** | System.Numerics.Vector3 |

