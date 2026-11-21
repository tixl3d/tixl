# VideoStreamInput

*in [Lib.io.video](README.md)*

Receives a live video stream from the network or from a publisher and outputs it as a texture.

Pulls a stream from `Url` (RTSP, RTMP, HTTP/MJPEG, HLS and anything else FFmpeg can open), for example from IP cameras, streaming servers or network video sources. Alternatively it hosts a small RTMP server: enable `RtmpServer` and push to the URL shown in `Status` from OBS, a phone streaming app, or ffmpeg. Any stream key is accepted while the server is listening.

Connecting and decoding run on a background thread, so a slow or unreachable source never stalls the editor. A source that sends no data for 10 seconds is treated as lost, and a lost or ended stream is reconnected automatically every few seconds while `Connect` (or `RtmpServer`) stays on. RTSP is pulled over TCP.

AKA: video stream, network video, ip camera, rtsp, rtmp, rtmp server, mjpeg, hls, obs, http stream, webcam stream

Tips:
- Use the `Connect` toggle to start and stop pulling from `Url`.
- Enable `RtmpServer` to receive a pushed RTMP stream instead; this takes precedence over `Connect`/`Url`.
- `Reconnect` forces a fresh connection right away instead of waiting for the automatic retry.
- `Status` shows connection problems and the RTMP server URL; the same message appears on the operator in the graph.
- Windows may ask for firewall permission the first time the RTMP server listens.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Connect** (Boolean) | Starts or stops pulling the stream from `Url`. While on, a lost stream is reconnected automatically. |
| **Reconnect** (Boolean) | A trigger that drops the current connection and connects again immediately. |
| **RtmpPort** (Int32) | The TCP port the built-in RTMP server listens on. Default is 1935, the standard RTMP port. Changing it restarts the server. |
| **Url** (String) | The URL of the network video stream (e.g. rtsp://192.168.1.100/stream1, rtmp://server/app/stream or http://camera/video.mjpg). Changing it reconnects. Ignored while `RtmpServer` is enabled. |
| **RtmpServer** (Boolean) | Enables the built-in RTMP server. It listens on `RtmpPort` and waits for a publisher (e.g. OBS); the address to push to is shown in `Status`. A publisher that disconnects can reconnect at any time. Disable it to stop the server. |


## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |
| **Resolution** | T3.Core.DataTypes.Vector.Int2 |
| **Status** | System.String |
