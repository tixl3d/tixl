# WebServer

*in [Lib.io.http](README.md)*

Starts a simple HTTP web server to serve content over the network.

This operator can be used to create simple web interfaces, provide data to other applications via a basic REST API, or serve files. The content served to connecting clients is determined by the `HtmlContent` input.

Tips:
- Use the `Listen` toggle to start and stop the server.
- If `Port` is set to 0, the server will automatically pick a free port.
- The server's URL will be http://[LocalIpAddress]:[Port]/

AKA: http, server, web, rest, api

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Listen** (Boolean) | Starts or stops the web server. |
| **LocalIpAddress** (String) | The local IP address for the server to listen on. A dropdown shows available addresses. Use '0.0.0.0' to listen on all available network interfaces. |
| **Port** (Int32) | The port number for the server to listen on. If set to 0, an available port will be chosen automatically. |
| **HtmlContent** (String) | The HTML, JSON, or plain text content to be served when a client connects to the server's root URL. |
| **PrintToLog** (Boolean) | If enabled, prints status information, incoming HTTP requests, and errors to the T3 log. Useful for debugging. |

## Outputs
| Name | Type |
|---|---|
| **IsRunning** | System.Boolean |
| **PortOutput** | System.Int32 |

