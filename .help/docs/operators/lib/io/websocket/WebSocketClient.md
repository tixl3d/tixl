# WebSocketClient

*in [Lib.io.websocket](README.md)*

Connects to a WebSocket server to enable real-time, two-way communication.

This operator establishes a client connection to a WebSocket server, allowing you to send and receive messages. It's useful for interacting with web-based applications, APIs, or other systems that use the WebSocket protocol.

Tips:
- The `Url` should start with 'ws://' or 'wss://' (for secure connections).
- Use `SendOnChange` or `SendTrigger` to control when messages are sent.
- Received messages can be accessed as a raw string, a list of lines, or parsed into a dictionary or list of parts, depending on the `ParsingMode`.

AKA: websocket, socket, web, real-time, client

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Connect** (Boolean) | Initiates or closes the connection to the WebSocket server. |
| **Delimiter** (String) | The character or string to use as a delimiter when `ParsingMode` is set to 'Split by Delimiter'. |
| **ListLength** (Int32) | The maximum number of received lines to store in the `ReceivedLines` output list. |
| **MessageParts** (String) | A list of strings or values that will be joined together to form the message to be sent. |
| **ParsingMode** (Int32) | Defines how incoming messages are parsed. Options include treating the message as a single string, splitting it into lines, parsing it as a JSON dictionary, or splitting it by a custom delimiter. |
| **PrintToLog** (Boolean) | If enabled, prints connection status, sent/received messages, and errors to the T3 log. Useful for debugging. |
| **SendOnChange** (Boolean) | If enabled, a message is sent automatically whenever the content of `MessageParts` changes. |
| **SendTrigger** (Boolean) | A trigger to manually send the message. The message is sent when this value changes from 0 to 1. |
| **Separator** (String) | A string used to join the `MessageParts` together before sending. |
| **Url** (String) | The URL of the WebSocket server (e.g., 'ws://localhost:8080'). |

## Outputs
| Name | Type |
|---|---|
| **IsConnected** | System.Boolean |
| **ReceivedDictionary** | T3.Core.DataTypes.Dict`1[System.Single] |
| **ReceivedLines** | System.Collections.Generic.List`1[System.String] |
| **ReceivedParts** | System.Collections.Generic.List`1[System.Single] |
| **ReceivedString** | System.String |
| **WasTrigger** | System.Boolean |

