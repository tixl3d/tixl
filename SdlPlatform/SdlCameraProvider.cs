using SDL;
using T3.Core.Logging;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>Cameras through SDL, which picks the native API of each platform and converts frames on its own thread.</summary>
public sealed unsafe class SdlCameraProvider : ICameraProvider
{
    public void EnsureStarted()
    {
        if (_isStarted)
        {
            // The camera set changed (seen by a capture thread or a camera listing); restart on the main thread.
            if (!_isRescanRequested)
                return;

            lock (_sdlLock)
            {
                // A capture thread is opening a camera. Restarting camera support would pull the subsystem
                // out from under it, and waiting here would block the caller — this runs on the evaluation
                // thread every frame. Leave the request standing for a later frame.
                if (_opensInFlight > 0 || _isShutDown)
                    return;

                _isRescanRequested = false;
                RestartCameraSupportLocked();
            }

            return;
        }

        _isStarted = true;
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_CAMERA))
            Log.Warning($"Could not start camera support: {SDL_GetError()}");

        lock (_sdlLock)
        {
            _knownDeviceSignature = CameraDeviceWatcher.ReadSignature();
        }
    }

    public IReadOnlyList<CameraInfo> GetCameras()
    {
        lock (_sdlLock)
        {
            if (_isShutDown)
                return [];

            // Someone is choosing a camera: pick up a newly connected one. The restart itself runs on the main
            // thread (EnsureStarted), because this may be called from a background task.
            if (HasCameraSetChangedLocked())
                _isRescanRequested = true;

            var cameras = ReadCameras();
            var result = new CameraInfo[cameras.Count];
            for (var index = 0; index < cameras.Count; index++)
            {
                var (name, cameraId) = cameras[index];
                result[index] = new CameraInfo(name, ReadModes(cameraId));
            }

            return result;
        }
    }

    public ICameraStream? TryOpen(string cameraName, CameraMode requestedMode, out string? error)
    {
        error = null;

        // Resolve what to open and reserve the open while holding the lock, then release it for the blocking
        // part: SDL_OpenCamera can sit for seconds on a permission prompt or a slow driver, and the
        // evaluation thread's EnsureStarted() must never wait behind that.
        SDL_CameraID cameraId;
        string displayName;
        CameraMode mode;
        int epoch;
        lock (_sdlLock)
        {
            if (_isShutDown)
            {
                error = "Camera support has shut down.";
                return null;
            }

            var cameras = ReadCameras();
            if (cameras.Count == 0)
            {
                error = "No camera found.";
                return null;
            }

            var cameraIndex = string.IsNullOrEmpty(cameraName) ? 0 : cameras.FindIndex(camera => camera.Name == cameraName);
            if (cameraIndex < 0)
            {
                if (HasCameraSetChangedLocked())
                {
                    // This runs on a capture thread; the main thread restarts camera support and the retry finds it.
                    _isRescanRequested = true;
                    error = $"Camera '{cameraName}' not found yet. Looking for newly connected cameras...";
                    return null;
                }

                error = $"Camera '{cameraName}' not found.";
                return null;
            }

            cameraId = cameras[cameraIndex].Id;
            displayName = cameras[cameraIndex].Name;
            mode = CompleteMode(requestedMode, ReadModes(cameraId));

            // No restart can run while this is set: EnsureStarted leaves the request standing instead.
            _opensInFlight++;
            epoch = _supportEpoch;
        }

        try
        {
            var spec = new SDL_CameraSpec
                           {
                               format = SDL_PIXELFORMAT_RGBA32,
                               colorspace = SDL_Colorspace.SDL_COLORSPACE_SRGB,
                               width = mode.Width,
                               height = mode.Height,
                               framerate_numerator = mode.Fps,
                               framerate_denominator = 1,
                           };

            SDL_Camera* camera;
            lock (_openLock)
            {
                camera = SDL_OpenCamera(cameraId, &spec);
            }

            if (camera == null)
            {
                error = $"Could not open camera '{displayName}': {SDL_GetError()}";
                return null;
            }

            lock (_sdlLock)
            {
                // Camera support restarted while this open was in flight, so the handle belongs to a
                // subsystem that no longer exists and nobody would ever close it.
                if (_isShutDown || epoch != _supportEpoch)
                {
                    SDL_CloseCamera(camera);
                    error = "Camera support restarted while the camera was opening.";
                    return null;
                }

                var stream = new Stream(this, camera, cameraId);
                _openStreams.Add(stream);
                return stream;
            }
        }
        finally
        {
            lock (_sdlLock)
            {
                _opensInFlight--;
            }
        }
    }

    /// <summary>
    /// Closes every open camera. Call before SDL_Quit: capture threads outlive the operators at exit, and SDL
    /// frees a camera out from under whichever thread is still reading it.
    /// </summary>
    public void Shutdown()
    {
        lock (_sdlLock)
        {
            _isShutDown = true;
            foreach (var stream in _openStreams)
            {
                stream.CloseLocked();
            }

            _openStreams.Clear();
        }
    }

    /// <summary>
    /// Where SDL has no hotplug (Windows, macOS), compares the system's camera list with the one SDL loaded.
    /// Throttled, because it is reached from retry loops and from dropdowns drawn every frame.
    /// </summary>
    private bool HasCameraSetChangedLocked()
    {
        if (!CameraDeviceWatcher.IsNeeded)
            return false;

        var now = Environment.TickCount64;
        if (now - _lastDeviceCheckMs < DeviceCheckIntervalMs)
            return false;

        _lastDeviceCheckMs = now;
        var signature = CameraDeviceWatcher.ReadSignature();
        return signature != null && signature != _knownDeviceSignature;
    }

    /// <summary>
    /// SDL reads the camera list only when camera support starts, so restarting it is the only way to see a
    /// camera connected later. Open cameras are closed first: their streams report Disconnected, and their
    /// operators reopen them within a second.
    /// </summary>
    private void RestartCameraSupportLocked()
    {
        foreach (var stream in _openStreams)
        {
            stream.CloseLocked();
        }

        _openStreams.Clear();

        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_CAMERA);
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_CAMERA))
            Log.Warning($"Could not restart camera support: {SDL_GetError()}");

        // Tells an open that is still in flight that the subsystem it opened against is gone.
        _supportEpoch++;
        _knownDeviceSignature = CameraDeviceWatcher.ReadSignature();
        Log.Debug("Camera list changed; camera support restarted to pick it up.");
    }

    /** Fills in what a request leaves open: the largest size that is cheap to convert, then that size's fastest rate. */
    private static CameraMode CompleteMode(CameraMode requested, IReadOnlyList<CameraMode> modes)
    {
        var width = requested.Width;
        var height = requested.Height;
        if (width <= 0 || height <= 0)
        {
            width = FallbackWidth;
            height = FallbackHeight;
            var bestPixelCount = 0;
            foreach (var mode in modes)
            {
                var pixelCount = mode.Width * mode.Height;
                if (mode.Height <= MaxDefaultHeight && pixelCount > bestPixelCount)
                {
                    width = mode.Width;
                    height = mode.Height;
                    bestPixelCount = pixelCount;
                }
            }
        }

        var fps = requested.Fps;
        if (fps <= 0)
        {
            foreach (var mode in modes)
            {
                if (mode.Width == width && mode.Height == height && mode.Fps > fps)
                    fps = mode.Fps;
            }

            if (fps <= 0)
                fps = FallbackFps;
        }

        return new CameraMode(width, height, fps);
    }

    private static List<(string Name, SDL_CameraID Id)> ReadCameras()
    {
        var result = new List<(string Name, SDL_CameraID Id)>();
        int count;
        var ids = SDL_GetCameras(&count);
        if (ids == null)
            return result;

        var seenNames = new Dictionary<string, int>();
        for (var index = 0; index < count; index++)
        {
            var name = SDL_GetCameraName(ids[index]) ?? "Camera";
            seenNames.TryGetValue(name, out var seenCount);
            seenNames[name] = ++seenCount;

            // Identical webcams report identical names, and the name is all a project stores.
            result.Add((seenCount == 1 ? name : $"{name} ({seenCount})", ids[index]));
        }

        SDL_free(ids);
        return result;
    }

    private static List<CameraMode> ReadModes(SDL_CameraID cameraId)
    {
        var result = new List<CameraMode>();
        int count;
        var specs = SDL_GetCameraSupportedFormats(cameraId, &count);
        if (specs == null)
            return result;

        for (var index = 0; index < count; index++)
        {
            var spec = specs[index];
            var fps = spec->framerate_denominator > 0 ? spec->framerate_numerator / spec->framerate_denominator : 0;
            var mode = new CameraMode(spec->width, spec->height, fps);
            if (!result.Contains(mode))
                result.Add(mode);
        }

        SDL_free(specs);
        return result;
    }

    private sealed class Stream(SdlCameraProvider provider, SDL_Camera* camera, SDL_CameraID cameraId) : ICameraStream
    {
        /// <summary>
        /// Per stream, so reading one camera does not serialize the other cameras' capture threads. Lock
        /// order is provider._sdlLock → _streamLock, never the reverse.
        /// </summary>
        private readonly Lock _streamLock = new();

        public CameraStreamState State => _state;

        public bool TryReadFrame(ref byte[] rgbaPixels, out int width, out int height)
        {
            width = 0;
            height = 0;

            lock (_streamLock)
            {
                if (_isClosed)
                {
                    _state = CameraStreamState.Disconnected;
                    return false;
                }

                UpdateState();
                if (_state != CameraStreamState.Open)
                    return false;

                ulong timestampNs;
                var frame = SDL_AcquireCameraFrame(camera, &timestampNs);
                if (frame == null)
                    return false;

                width = frame->w;
                height = frame->h;
                var rowByteCount = width * BytesPerPixel;
                var byteCount = rowByteCount * height;
                if (rgbaPixels.Length < byteCount)
                    rgbaPixels = new byte[byteCount];

                fixed (byte* destination = rgbaPixels)
                {
                    var source = (byte*)frame->pixels;
                    if (frame->pitch == rowByteCount)
                    {
                        // The usual case for RGBA32 — one copy instead of one per row.
                        Buffer.MemoryCopy(source, destination, byteCount, byteCount);
                    }
                    else
                    {
                        for (var row = 0; row < height; row++)
                        {
                            Buffer.MemoryCopy(source + row * frame->pitch, destination + row * rowByteCount, rowByteCount, rowByteCount);
                        }
                    }
                }

                SDL_ReleaseCameraFrame(camera, frame);
                return true;
            }
        }

        public void Dispose()
        {
            lock (provider._sdlLock)
            {
                CloseLocked();
                provider._openStreams.Remove(this);
            }
        }

        public void CloseLocked()
        {
            lock (_streamLock)
            {
                if (_isClosed)
                    return;

                _isClosed = true;
                SDL_CloseCamera(camera);
            }
        }

        /** Checked regardless of frames arriving, because SDL keeps delivering blank frames from an unplugged camera. */
        private void UpdateState()
        {
            var now = Environment.TickCount64;
            if (now - _lastStateCheckMs < StateCheckIntervalMs)
                return;

            _lastStateCheckMs = now;

            if (SDL_GetCameraPermissionState(camera) == SDL_CameraPermissionState.SDL_CAMERA_PERMISSION_STATE_DENIED)
            {
                _state = CameraStreamState.Denied;
                return;
            }

            int count;
            var ids = SDL_GetCameras(&count);
            if (ids == null)
                return;

            var isConnected = false;
            for (var index = 0; index < count && !isConnected; index++)
            {
                isConnected = ids[index] == cameraId;
            }

            SDL_free(ids);
            if (!isConnected)
                _state = CameraStreamState.Disconnected;
        }

        private volatile CameraStreamState _state = CameraStreamState.Open;
        private long _lastStateCheckMs = Environment.TickCount64;
        private bool _isClosed;
    }

    private const int BytesPerPixel = 4;
    private const int FallbackWidth = 640;
    private const int FallbackHeight = 480;
    private const int FallbackFps = 30;

    /** Frames are converted and uploaded on the CPU, which sensor-sized modes can't sustain for every user by default. */
    private const int MaxDefaultHeight = 1080;

    private const int StateCheckIntervalMs = 1000;
    private const int DeviceCheckIntervalMs = 1000;

    private readonly Lock _sdlLock = new();

    /** Serializes SDL_OpenCamera itself, which runs without the registry lock so it cannot block EnsureStarted. */
    private readonly Lock _openLock = new();

    private readonly List<Stream> _openStreams = [];

    /** Opens that resolved their camera but have not finished SDL_OpenCamera yet. Guarded by _sdlLock. */
    private int _opensInFlight;

    /** Bumped by every restart, so an open that outlived it knows its camera handle is void. */
    private int _supportEpoch;

    private bool _isStarted;
    private bool _isShutDown;
    private volatile bool _isRescanRequested;
    private string? _knownDeviceSignature;
    private long _lastDeviceCheckMs;
}
