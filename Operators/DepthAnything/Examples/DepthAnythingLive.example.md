# Live depth from an RTSP camera

Connects to a network camera and estimates depth on every delivered frame.

- `VideoStreamInput` connects to `rtsp://10.0.0.189:8554/rpi_webcam` (set `Connect` on, it is saved on).
- `DepthAnything` runs V3 Small on every delivered frame; the example outputs the colorized
  `NormalizedDepthTexture` (near objects bright).
- `StreamUrl` is exposed on the example so you can point it at any other stream without opening the
  graph; the VideoStreamInput's `Status` output reports the connection state.

## How it works
The stream delivers frames asynchronously: when no frame has arrived yet the DepthAnything outputs
stay empty and `Status` shows the connection attempt. Depth inference is asynchronous too — the
depth trails the camera by a frame or two. `UpdateCount` only advances on frames the worker has
actually processed, so it doubles as a "frames analyzed" counter.

## Files
`Operators/Examples/Symbols/Lib/io/video/depthanything/DepthAnythingLiveExample.{cs,t3,t3ui}`
