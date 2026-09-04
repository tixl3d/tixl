# ArtnetPixelOutput

*in [Lib.io.dmx.obsolete](README.md)*

Converts a list of 3D points into Art-Net DMX data for controlling RGB pixel arrays or similar fixtures.

This operator takes a buffer of points, extracts their color information (RGB from `Point.Color.X/Y/Z`), and assigns them to DMX universes based on the `Point.F2` attribute. It then sends this data as Art-Net packets over the network.

Tips:
- Ensure the `IpAddress` is set to the correct Art-Net node or broadcast address.
- The `ArtNetSendRate` controls how frequently DMX data is transmitted.
- Use the `Reconnect` trigger if the network connection is lost or the IP address changes.

AKA: artnet, pixel, rgb, dmx, obsolete

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | A buffer of 3D points. The `Color` attribute (RGB) of each point is used for DMX values, and the `F2` attribute determines the DMX universe. |
| **IpAddress** (String) | The target IP address of the Art-Net receiver or node (e.g., '192.168.1.100' or '255.255.255.255' for broadcast). |
| **Reconnect** (Boolean) | A trigger to force a reconnection to the Art-Net target. The connection is re-established when this value changes from 0 to 1. |
| **ArtNetSendRate** (Single) | The rate in Hertz (frames per second) at which Art-Net packets are sent. Set to 0 for maximum rate. Default is 44.0. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

