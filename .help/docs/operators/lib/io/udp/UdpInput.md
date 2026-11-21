# UdpInput

*in [Lib.io.udp](README.md)*

Receives data from the network using the UDP (User Datagram Protocol).

This operator listens for UDP packets on a specified port, allowing communication with other applications or devices that send UDP data. UDP is a connectionless protocol, meaning data is sent without establishing a persistent connection, which is fast but less reliable than TCP.

Tips:
- Ensure the `Port` matches the sender's destination settings; pick the listening adapter on the Settings window's Networking page.
- `WasTrigger` becomes true for one frame whenever a new message is received, which can be used to trigger other actions.
- Unlike TCP, UDP does not guarantee message delivery or order.

AKA: udp, network, socket, datagram

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Listen** (Boolean) | Enables or disables listening for UDP packets. |
| **ListLength** (Int32) | The maximum number of received lines to store in the `ReceivedLines` output list. |
| **Port** (Int32) | The UDP port to listen for messages on. |
| **PrintToLog** (Boolean) | If enabled, prints received data and sender information to the T3 log. Useful for debugging. |

## Outputs
| Name | Type |
|---|---|
| **IsListening** | System.Boolean |
| **LastSenderAddress** | System.String |
| **LastSenderPort** | System.Int32 |
| **ReceivedLines** | System.Collections.Generic.List`1[System.String] |
| **ReceivedString** | System.String |
| **WasTrigger** | System.Boolean |

