# PosiStageOutput

*in [Lib.io.posistage](README.md)*

Sends real-time 3D tracking data using the PosiStageNet (PSN) protocol.

This operator takes a list of points and sends them as PosiStageNet tracker data over the network. This allows T3 to act as a source of tracking information for other applications in a virtual production or live event workflow.

Tips:
- The `TrackerData` input expects a structured buffer of points, where each point represents a tracker with position and orientation.
- Use `SendOnChange` or `SendTrigger` to control when data is sent.
- The `Names` input can be used to assign names to the trackers being sent.

AKA: posistage, tracking, stage, psn, motion capture, sender

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Connect** (Boolean) | Initializes or re-initializes the network socket. |
| **LocalIpAddress** (String) | The IP address of the network interface to send from. If left empty, the system will choose one automatically. |
| **Names** (String) | A list of strings to name the individual trackers. The list should correspond to the points in `TrackerData`. |
| **PrintToLog** (Boolean) | If enabled, prints information about sent packets to the T3 log for debugging. |
| **SendOnChange** (Boolean) | If enabled, sends a packet automatically whenever the `TrackerData` input changes. |
| **SendTrigger** (Boolean) | A trigger to manually send a PosiStageNet packet. |
| **ServerName** (String) | A name for this PosiStageNet source, which can help identify it on the receiving end. |
| **TargetIpAddress** (String) | The destination IP address for the PSN packets. This is often a multicast address like 236.10.10.10. |
| **TargetPort** (Int32) | The destination UDP port. The default for PSN is typically 12345. |
| **TrackerData** (BufferWithViews) | The input for the structured buffer of points representing the trackers. Each point should have position (Position) and orientation (Rotation). |

## Outputs
| Name | Type |
|---|---|
| **Command** | T3.Core.DataTypes.Command |
| **IsConnected** | System.Boolean |

