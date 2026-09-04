# TcpServer

*in [Lib.io.tcp](README.md)*

Creates a TCP server to listen for incoming connections and receive data.

This operator starts a TCP server on a specified IP address and port, allowing multiple clients to connect simultaneously. It receives data from all connected clients and can broadcast messages to them.

Tips:
- Use `Listen` to start and stop the server.
- `ConnectionCount` shows how many clients are currently connected.
- Messages sent via the `Message` input are broadcast to all connected clients.

AKA: tcp, network, socket, server

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Listen** (Boolean) | Starts or stops the TCP server from listening for new connections. |
| **LocalIpAddress** (String) | The local IP address for the server to listen on. A dropdown shows available addresses. Using '0.0.0.0' will make the server listen on all available network interfaces. |
| **Message** (String) | The message to be broadcast to all connected clients. |
| **Port** (Int32) | The port number for the server to listen on. |
| **PrintToLog** (Boolean) | If enabled, prints status information, connection events, sent/received data, and errors to the T3 log. Useful for debugging. |
| **SendOnChange** (Boolean) | If enabled, the `Message` is sent automatically to all clients whenever its content changes. |
| **SendTrigger** (Boolean) | A trigger to manually send the `Message` to all connected clients. The message is sent when this value changes from 0 to 1. |

## Outputs
| Name | Type |
|---|---|
| **ConnectionCount** | System.Int32 |
| **IsListening** | System.Boolean |
| **Result** | T3.Core.DataTypes.Command |

