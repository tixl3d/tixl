# TcpClient

*in [Lib.io.tcp](README.md)*

Connects to a TCP server to send and receive data.

This operator establishes a client connection to a specified TCP server. It can send messages constructed from various parts and receives data from the server, which can be processed as a continuous string or as a list of lines.

Tips:
- Use `SendOnChange` to automatically send data whenever the `MessageParts` input changes.
- For manual control over sending, use the `SendTrigger` input.
- Check the `IsConnected` output to verify the connection status before attempting to send data.

AKA: tcp, network, socket, client

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Connect** (Boolean) | Initiates or closes the connection to the TCP server. |
| **Host** (String) | The IP address or hostname of the TCP server to connect to (e.g., '127.0.0.1' or 'localhost'). |
| **ListLength** (Int32) | The maximum number of received lines to store in the `ReceivedLines` output list. |
| **MessageParts** (String) | A list of strings or values that will be joined together to form the message to be sent. |
| **Port** (Int32) | The port number on the TCP server to connect to. |
| **PrintToLog** (Boolean) | If enabled, prints status information, sent/received data, and errors to the T3 log. Useful for debugging. |
| **SendOnChange** (Boolean) | If enabled, a message is sent automatically whenever the content of `MessageParts` changes. |
| **SendTrigger** (Boolean) | A trigger to manually send the message. The message is sent when this value changes from 0 to 1. |
| **Separator** (String) | A string used to join the `MessageParts` together before sending. |
| **LocalIpAddress** (String) | — |

## Outputs
| Name | Type |
|---|---|
| **IsConnected** | System.Boolean |
| **ReceivedLines** | System.Collections.Generic.List`1[System.String] |
| **ReceivedString** | System.String |
| **WasTrigger** | System.Boolean |

