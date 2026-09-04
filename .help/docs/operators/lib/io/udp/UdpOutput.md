# UdpOutput

*in [Lib.io.udp](README.md)*

Sends data to the network using the UDP (User Datagram Protocol).

This operator sends UDP packets to a specified IP address and port. UDP is a connectionless protocol, which is fast but does not guarantee the delivery or order of messages. Messages can be constructed from multiple parts.

Tips:
- Use `SendOnChange` to automatically send data when `MessageParts` changes.
- Use `SendTrigger` for manual control over sending messages.
- To broadcast to all devices on the local network, use a broadcast IP address like '255.255.255.255'.

AKA: udp, network, socket, datagram

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Connect** (Boolean) | Initializes or re-initializes the network socket. This can be useful if network settings change. |
| **LocalIpAddress** (String) | The IP address of the network interface to send from. A dropdown shows available addresses. If left empty, the system will choose one automatically. |
| **MessageParts** (String) | A list of strings or values that will be joined together to form the message to be sent. |
| **PrintToLog** (Boolean) | If enabled, prints sent data and status information to the T3 log. Useful for debugging. |
| **SendOnChange** (Boolean) | If enabled, a message is sent automatically whenever the content of `MessageParts` changes. |
| **SendTrigger** (Boolean) | A trigger to manually send the message. The message is sent when this value changes from 0 to 1. |
| **Separator** (String) | A string used to join the `MessageParts` together before sending. |
| **TargetIpAddress** (String) | The destination IP address to send the UDP packet to (e.g., '127.0.0.1' for localhost). Use a broadcast address (e.g., '255.255.255.255') to send to all devices on the network. |
| **TargetPort** (Int32) | The UDP port on the destination device to send messages to. |

## Outputs
| Name | Type |
|---|---|
| **IsConnected** | System.Boolean |
| **Result** | T3.Core.DataTypes.Command |

