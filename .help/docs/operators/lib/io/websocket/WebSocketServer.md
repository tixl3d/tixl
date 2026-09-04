# WebSocketServer

*in [Lib.io.websocket](README.md)*

Creates a WebSocket server to enable real-time, two-way communication with web clients.

This operator is ideal for building interactive web-based UIs, remote controls, or dashboards that can send data to and receive data from T3. It can broadcast messages to all connected clients and parse incoming messages from them.

Tips:
- Use `Listen` to start and stop the server.
- The server address will be `ws://[LocalIpAddress]:[Port]/[Path]`.
- Messages sent from T3 via the `Message` input are broadcast to all connected clients.
- Incoming messages from clients are available on the `LastReceived...` outputs.

AKA: websocket, server, network, web, socket, real-time

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Listen** (Boolean) | Starts or stops the WebSocket server. |
| **Port** (Int32) | The port number for the server to listen on. Default is 8080. |
| **Path** (String) | The URL path for the WebSocket endpoint (e.g., '/t3-socket'). Default is empty (root path). |
| **Message** (String) | The message to be broadcast to all connected clients. |
| **SendOnChange** (Boolean) | If enabled, the `Message` is broadcast automatically to all clients whenever its content changes. |
| **SendTrigger** (Boolean) | A trigger to manually broadcast the `Message` to all connected clients. The message is sent when this value changes from 0 to 1. |
| **LocalIpAddress** (String) | The local IP address to bind the server to. Use '0.0.0.0 (Listen on all interfaces)' to listen on all available network interfaces. Default is '0.0.0.0 (Listen on all interfaces)'. |
| **PrintToLog** (Boolean) | If enabled, prints connection status, sent/received messages, and errors to the T3 log. Useful for debugging. |
| **ClientMessageParsingMode** (Int32) | Defines how incoming messages from clients are parsed. 'Raw' treats the message as a single string, while other modes attempt to split or parse the message. Default is Raw. |
| **ClientMessageDelimiter** (String) | The character or string to use as a delimiter when `ClientMessageParsingMode` is set to 'SpaceSeparated'. Default is a single space. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |
| **IsListening** | System.Boolean |
| **ConnectionCount** | System.Int32 |
| **LastReceivedClientString** | System.String |
| **LastReceivedClientParts** | System.Collections.Generic.List`1[System.Single] |
| **LastReceivedClientDictionary** | T3.Core.DataTypes.Dict`1[System.Single] |
| **ClientMessageWasTrigger** | System.Boolean |

