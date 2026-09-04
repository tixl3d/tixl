# PosiStageInput

*in [Lib.io.posistage](README.md)*

Receives real-time 3D tracking data using the PosiStageNet protocol.

This operator listens for PosiStageNet (PSN) packets, which are commonly used in live events and virtual production to stream tracking data for cameras, objects, and performers.

Tips:
- Ensure your tracking system is sending PSN data to the specified `MulticastIpAddress` and `Port`.
- The output `TrackersAsBuffer` provides the raw tracking data as a structured buffer, which is efficient for use with other operators that process point data.
- `TrackersAsDict` provides the data as a dictionary, which can be easier for inspecting and extracting specific tracker information.

AKA: posistage, tracking, stage, psn, motion capture

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Listen** (Boolean) | Enables or disables listening for PosiStageNet packets. |
| **LocalIpAddress** (String) | The IP address of the network interface to listen on. A dropdown shows available addresses. Ensure this is the interface connected to your tracking network. |
| **MulticastIpAddress** (String) | The multicast IP address to join for receiving PosiStageNet data. The default for PSN is typically 236.10.10.10. |
| **Port** (Int32) | The UDP port to listen for PosiStageNet data on. The default for PSN is typically 12345. |
| **PrintToLog** (Boolean) | If enabled, prints received tracker data and status information to the T3 log. Useful for debugging. |

## Outputs
| Name | Type |
|---|---|
| **TrackersAsBuffer** | T3.Core.DataTypes.BufferWithViews |
| **TrackersAsDict** | T3.Core.DataTypes.Dict`1[System.Single] |
| **IsListening** | System.Boolean |

