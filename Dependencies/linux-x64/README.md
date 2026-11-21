# Native libraries for Linux (x86-64)

Shared objects here are copied into the Editor and Player output on Linux (see the `linux-x64` item groups in
`Editor/Editor.csproj` and `Player/Player.csproj`). Windows keeps its `.dll` files one folder up.

`libtixlaudio.so` is built from `Native/TixlAudio/`; see the README there.

## MediaPipe

| File                | Source |
|---------------------|--------|
| `libmediapipe_c.so` | homuler/MediaPipeUnityPlugin v0.16.3 release (`MediaPipeUnityPlugin-all-stripped.zip`, `Runtime/Plugins/libmediapipe_c.so`) |

The vendored binding (`Operators/Mediapipe/Vendor`) P/Invokes the bare name `mediapipe_c`, which .NET
resolves to this file on Linux, `mediapipe_c.dll` on Windows and `libmediapipe_c.dylib` on macOS. The Linux
build is the GPU-enabled one: loading it needs `libEGL.so.1` and `libGLESv2.so.2` (mesa), which any Linux
desktop with GPU drivers has - TiXL needs Vulkan anyway. Apache 2.0, like the rest of MediaPipe; the
licence text lives in `Dependencies/licenses`.
