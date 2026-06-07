# Plan — `[VideoDeviceInput]` OpenCV native-init crash (deferred)

**Status:** Deferred 2026-06-03. Webcam capture via `[VideoDeviceInput]` throws on first use.
Not a release blocker. `[VideoDeviceInput]` is the **last remaining consumer of OpenCV's
`VideoCapture`** after the FFmpeg migration ported `VideoStreamInput` off OpenCV.

## Symptom

Dropping/using a `[VideoDeviceInput]` (webcam) op throws on the capture thread:

```
The type initializer for 'OpenCvSharp.Internal.NativeMethods' threw an exception.
   at OpenCvSharp.Internal.NativeMethods.videoio_VideoCapture_new1(IntPtr& returnValue)
   at OpenCvSharp.VideoCapture..ctor()
   at Lib.io.video.VideoDeviceInput.CaptureLoop(...) in VideoDeviceInput.cs:line 229
```

It's caught on the worker thread (the op shows an error status); the editor does not crash.

## This is NOT a regression from the FFmpeg migration (ruled out)

- `[VideoDeviceInput]` still uses OpenCV: `new VideoCapture()` then `.Open(index, DSHOW)`
  ([VideoDeviceInput.cs:229](../../Operators/Lib/io/video/VideoDeviceInput.cs)). We deliberately
  did not port it.
- The failure is in OpenCV's **`NativeMethods` static constructor** (which loads
  `OpenCvSharpExtern.dll`). That runs the first time *any* OpenCV type is touched — **before** any
  video backend is selected. It is unrelated to the ffmpeg backend plugin we removed and to the
  DShow webcam backend.
- `OpenCvSharpExtern.dll` is present (62 MB) in both the operator output and the editor output, and
  its import table — checked with `dumpbin /dependents` **and** `/imports` (delay-load included) —
  references **only Windows system DLLs** (`KERNEL32`, `MFPlat`/`MF`/`MFReadWrite`, `d3d11`, `dxgi`,
  `CRYPT32`, `WS2_32`, …) and **zero** ffmpeg libraries. So the FFmpeg 7.0 `av*.dll` we co-located in
  `runtimes/win-x64/native/` cannot shadow or break OpenCV's load.
- We removed only `opencv_videoio_ffmpeg4110_64.dll` — a *lazy* `CAP_FFMPEG` backend plugin, never
  loaded by the cctor and never used by the webcam (`CAP_DSHOW`) path.

## Most likely root cause (to confirm)

Native-DLL resolution of `OpenCvSharpExtern.dll` inside TiXL's custom `AssemblyLoadContext`. The DLL
ships **only** under `runtimes/win-x64/native/`, but TiXL's `LoadUnmanagedDll` resolves
operator-package natives **flat** from `MainDirectory`
([TixlAssemblyLoadContext.cs:465-509](../../Core/Compilation/TixlAssemblyLoadContext.cs)); the
`NativeDllResolver` ([:442-461](../../Core/Compilation/TixlAssemblyLoadContext.cs)) tries
`NativeLibrary.TryLoad(name, assembly)` then a Root-assembly fallback. If none of those locate
`runtimes/win-x64/native/OpenCvSharpExtern.dll`, the P/Invoke fails → `DllNotFoundException` →
`TypeInitializationException`.

**Open question that decides this:** other OpenCV ops (`CameraCalibrator`, etc.) link the *same*
`OpenCvSharpExtern.dll`. Do they work in this environment? If they do, resolution is not the cause —
look instead at a missing dependency of `OpenCvSharpExtern.dll` (a VC++ runtime, or a Media
Foundation component — note its `MFPlat`/`MF`/`MFReadWrite` imports) or at the shadow-copy step
skipping the native sub-directory.

## Diagnostic already in place

[VideoDeviceInput.cs:365](../../Operators/Lib/io/video/VideoDeviceInput.cs) now unwraps and logs the
**inner** exception (it previously logged only the generic outer message, which is why the original
report was uninformative). The next reproduction will print the real cause — `DllNotFoundException`
vs `BadImageFormatException` vs a named missing dependency. **Start here:** it selects the branch
below. (Requires an editor restart / operator recompile to take effect.)

## Options

1. **Fix OpenCV native resolution in the ALC** — if the inner exception is `DllNotFound` for
   `OpenCvSharpExtern`. Make the `runtimes/win-x64/native/` DLLs discoverable: either probe
   `runtimes/<rid>/native/` in `NativeDllResolver`, or flatten `OpenCvSharpExtern.dll` +
   `opencv_videoio_ffmpeg*.dll` into the output root the way we flatten the FFmpeg DLLs. Lowest
   effort if resolution is the cause; keeps OpenCV. **Caveat:** flattening OpenCvSharpExtern is a
   62 MB copy and affects every OpenCV op, so prefer the resolver-probe fix.
2. **Port the webcam off OpenCV onto FFmpeg's `dshow` input** — open `video=<device name>` through a
   `VideoDecoderSession` with the `dshow` demuxer; enumerate devices via `DirectShowLib.Standard`
   (already referenced in `Lib.csproj`). Removes the last OpenCV `VideoCapture` dependency, aligns
   with retiring OpenCV video I/O, and reuses the FFmpeg decode stack + `SoftwareFrameConverter`
   already built. Larger effort; the `VideoStreamInput` port is the template (swap the RTSP URL +
   `rtsp_transport` options for a `dshow` URL + `video_size`/`framerate` options).
3. **Document a missing system prerequisite** — if the inner exception is a missing VC++ redist or
   Media Foundation component (e.g. Windows N without the Media Feature Pack). Not a code fix.

## Recommendation

Get the inner exception first. If it's resolution → option 1 (cheap, keeps OpenCV). Longer term,
option 2 (FFmpeg `dshow`) is the clean end state since OpenCV video I/O is being retired — but only
worth doing if webcams are actually needed; otherwise leave deferred.

## Files

- `Operators/Lib/io/video/VideoDeviceInput.cs` — the operator (inner-exception logging already added).
- `Core/Compilation/TixlAssemblyLoadContext.cs` — native resolver (option 1).
- `Video/VideoDecoderSession.cs` + `Video/SoftwareFrameConverter.cs` — reuse for option 2.
