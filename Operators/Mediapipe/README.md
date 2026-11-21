# Mediapipe operator package

Seven vision operators wrapping the MediaPipe Tasks C API through the vendored binding in `Vendor/`.

| Operator | Model | Notes |
|----------|-------|-------|
| [FaceDetection](Symbols/Lib/io/video/mediapipe/FaceDetection.cs) | `blaze_face_short_range.tflite` | bounding box + 6 keypoints per face |
| [FaceLandmarkDetection](Symbols/Lib/io/video/mediapipe/FaceLandmarkDetection.cs) | `face_landmarker.task` | 478 landmarks + blendshapes |
| [GestureRecognition](Symbols/Lib/io/video/mediapipe/GestureRecognition.cs) | `gesture_recognizer.task` | gesture label + handedness |
| [HandLandmarkDetection](Symbols/Lib/io/video/mediapipe/HandLandmarkDetection.cs) | `hand_landmarker.task` | 21 landmarks, image and world space |
| [ImageSegmentation](Symbols/Lib/io/video/mediapipe/ImageSegmentation.cs) | `selfie_segmenter.tflite`, `selfie_multiclass_256x256.tflite`, `deeplabv3.tflite` | category + confidence masks |
| [ObjectDetection](Symbols/Lib/io/video/mediapipe/ObjectDetection.cs) | `efficientdet_lite0/lite2.tflite` | label allow/deny lists |
| [PoseLandmarkDetection](Symbols/Lib/io/video/mediapipe/PoseLandmarkDetection.cs) | `pose_landmarker.task` | 33 landmarks, optional segmentation mask |

The operator documentation the editor shows is generated from each symbol's `.t3ui` description; the
user-facing reference lives in `.help/docs/operators/lib/io/video/mediapipe/`.

Installed as a package, `Symbols/` and the operators' sources are mirrored into `SourceCode/`; the
`Utils/` helpers are not part of that mirror (the editor only uses it to map a symbol to the file it came
from, and warns about every file there that declares no operator).
## Threading model

An operator never runs inference on the evaluation thread. `Update` only does three things:

1. read the input texture back into a pinned byte buffer (staging texture + row-pitch aware downsample to
   at most 640 px wide),
2. push a request onto a `ConcurrentQueue`,
3. drain finished results and write the output slots.

One worker task per operator owns the native instance and drains its request queue to the **newest**
frame before each inference, so latency does not accumulate and the queue cannot grow without bound.
`_workerBusy` gates the readback so no frame is captured while an inference is running.

The readback itself is shared: `Utils/MediaPipeFrameCache` copies the source texture once per evaluation
frame (`Playback.FrameCount` is the validity token, the same one the audio engine uses) and downsamples it
to 640 px, and every operator reading the same texture then copies from that array into its own pooled
buffer. Measured with seven operators on one camera: 11.3 ms/frame of readback became 3.6 ms at 1080p
(3.2x), 6.4 -> 2.3 ms at 720p. The cache holds the arrays across frames and rebuilds only when the size or
the frame changes, so the steady state allocates nothing.
XNNPACK is the acceleration that matters on every platform. `InferenceThreads` defaults to 4 - the
binding's 0 ("library default") is effectively single-threaded and measured 20.6 ms/frame against 10.9 ms
at 4 on a 20-core machine; 8 adds about 10% for double the threads. Graphs driving several operators from
one camera should lower it to 2 each, which is what the example does.
Every dequeued result replaces the previous one — including an empty result, which clears the outputs.
Holding the last non-empty result would latch `FaceCount`/`PointBuffer` (etc.) onto the last face ever
seen instead of reporting that it left the frame.

### Input format

The downsample walks the mapped staging copy four bytes at a time and takes the first three as R, G and B,
which is only valid for an **8-bit four-channel** texture (BGRA/RGBA8 — what cameras, video inputs and
render targets in TiXL deliver). Anything else is refused once with a warning and the image is passed
through: a half-float surface would produce nonsense landmarks, and a narrower one would make the walk read
past the mapping. The staging cache is keyed on `(width, height, format)` so a same-size format change can
never reuse a texture the copy would reject.


### Native task lifetime

The task APIs block inside native inference and offer no cancellation. Closing a task while a worker is
inside `DetectForVideo` is a use-after-free in native code — a hard process crash no managed handler can
catch. `Utils/MediaPipeWorkerReaper.cs` owns that hand-off:

- `Queue(worker, native, cts)` waits for the worker **on the thread pool**, then releases the native task;
- teardowns chain, so two native instances of one operator never close concurrently;
- `CloseNative` calls `IDisposable.Dispose()` (which also frees the native `TaskRunner`) rather than the
  public `Close()`, which only closes the calculator graph and leaks one runner per restart;
- `WaitForPending(2000)` in `Dispose` bounds how long shutdown waits for a wedged library — it never hangs.

### Initialization and failure

The native instance is created on the worker (`InitializeAndRunWorker`), never under the evaluation lock,
so a slow model load cannot stall a frame. If initialization fails (missing model, missing native
library, rejected delegate) the operators back off exponentially — 1 s, 2 s, 4 s … capped at 30 s
(`MediaPipeInitRetry.BackoffMs`) — and retry immediately when a parameter changes. Failures always log a
warning, whether or not `Debug` is set: a missing asset is the one failure a user has to act on, and
"nothing happens" with no log is the worst outcome. `Debug` only adds the verbose per-frame messages and
the debug texture.

`Gpu` falls back to CPU with a logged warning on any native that cannot serve the delegate; the CPU paths
are always available.

A disposed instance ignores `Update`. Without that guard a late evaluation would start a worker that
nothing ever stops — its native model and its polling thread would survive for the process lifetime.

### Memory

Everything the operators allocate per frame comes from a bounded pool and is returned on every path,
including teardown:

- pixel buffers are pinned on the POH (the native `Image` keeps the pointer past the constructor) and
  pooled up to `MaxPooledBuffers` per operator;
- `Point[]`/`Detection[]` results are pooled by size and returned when a newer result replaces them;
  the detection pool is what `ConvertDetectionResultToDetections` rents from, and it is capped per size;
- `Mat` objects are not pooled — the debug readback creates one and the `using` at the call site disposes
  it, which is cheaper than validating a pooled Mat's dimensions on every frame;
- staging textures are cached per (width, height, format) and the cache is capped, because a resized
  window or a switched camera yields a new size per step;
- segmentation masks returned by the native are disposed after copying — leaking them cost ~0.9 MB per
  frame at 640×360. The resulting mask bytes are plain arrays and are deliberately **not** returned to the
  inference-buffer pool, which only holds POH-pinned inputs.

## Native libraries

`Vendor/PInvoke/LibName.cs` P/Invokes the bare name `mediapipe_c`, which .NET resolves per platform from
the application directory:

| Platform | File | Source | GPU delegate |
|----------|------|--------|--------------|
| Windows | `Dependencies/mediapipe_c.dll` | homuler/MediaPipeUnityPlugin v0.16.3 | no (CPU-only build) |
| Linux | `Dependencies/linux-x64/libmediapipe_c.so` | same release | yes (needs `libEGL.so.1`, `libGLESv2.so.2`) |
| macOS | `Dependencies/osx/libmediapipe_c.dylib` | same release, re-signed | no; needs `libmediapipe_opencl_stub.dylib` |

`Editor/Editor.csproj` and `Player/Player.csproj` copy the platform's native from `Dependencies/` and build
the macOS OpenCL stub (`BuildMediapipeOpenClStub`, needs clang). See the READMEs in those folders for the
details of the macOS patches. `MediapipeNativeLibrary` (a module initializer) loads the stub before the
first P/Invoke and registers `MediaPipeAcceleration` as an input slot type.

`ExportDependencies` on each operator lists the native files, so exported projects carry them along.

## Tests

`Mediapipe.Tests/` (solution folder *Tests*):

```bash
dotnet test Mediapipe.Tests/Mediapipe.Tests.csproj -c Debug
```

- `MediapipeNativeTests` drives the real shipped native through the Tasks API on every platform — CPU,
  Xnnpack and GPU delegates, VIDEO-mode timestamp validation, mask read/dispose across frames. These catch
  a native that fails to load, drifting exports, or an ABI mismatch.
- `MediaPipeWorkerReaperTests` pins the teardown contract: native task closed only after its worker exits,
  faulted/cancelled workers still release, consecutive teardowns are serialised, `WaitForPending` is
  bounded, and the init backoff curve is asserted against the same `MediaPipeInitRetry.BackoffMs` the
  operators call (`MediaPipeInitRetryTests`).

The tests link the models from `Operators/Mediapipe/Assets` into their output and only need the platform
native; no GPU and no editor.

## Adding an operator

1. Copy the closest existing operator and change the model in `Assets/`.
2. Keep the shape: `_inputQueue`/`_outputQueue`, `_workerBusy`, `InitializeAndRunWorker` on the worker,
   `StopWorker` through `_reaper`, `_disposed` in `Update`, the `MediaPipeTextureReadback` gate at the top
   of the readback, `MediaPipeInitRetry.BackoffMs` for retries, pooled buffers and results, and
   `ClearOutputs` on every early return.
3. Add `[ExportDependencies]` with every native the operator needs.
4. Declare inputs with stable GUIDs; add a `.t3` default and a `.t3ui` row (position, description, range)
   for each, so the parameter panel and the generated docs stay in sync.
5. Add a symbol to `Operators/Examples/Symbols/Lib/io/video/mediapipe/` and a case to the native tests.
