# VideoStreamInput

*in [Lib.io.video](README.md)*

Receives a video stream from a network source.

This operator connects to a network video stream (e.g., RTSP, HTTP, RTMP) and provides the video as a texture. It is useful for bringing in live feeds from IP cameras, streaming servers, or other network video sources.

Tips:
- Use the `Connect` toggle to start and stop receiving the stream.
- If the stream is interrupted, use the `Reconnect` trigger to try and establish the connection again.
- The `Status` output provides valuable information for debugging connection issues.

AKA: video stream, rtsp, rtmp, http, ip camera, network video

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Connect** (Boolean) | Starts or stops the connection to the video stream. |
| **Reconnect** (Boolean) | A trigger to force a reconnection to the video stream. |
| **Url** (String) | The URL of the network video stream (e.g., rtsp://192.168.1.100/stream1). |

## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |
| **Resolution** | T3.Core.DataTypes.Vector.Int2 |
| **Status** | System.String |

