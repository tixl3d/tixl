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
            return;

        _isStarted = true;
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_CAMERA))
            Log.Warning($"Could not start camera support: {SDL_GetError()}");
    }

    public IReadOnlyList<CameraInfo> GetCameras()
    {
        lock (_sdlLock)
        {
            if (_isShutDown)
                return [];

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
        lock (_sdlLock)
        {
            return TryOpenLocked(cameraName, requestedMode, out error);
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

    private ICameraStream? TryOpenLocked(string cameraName, CameraMode requestedMode, out string? error)
    {
        error = null;
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
            error = $"Camera '{cameraName}' not found.";
            return null;
        }

        var cameraId = cameras[cameraIndex].Id;
        var mode = CompleteMode(requestedMode, ReadModes(cameraId));
        var spec = new SDL_CameraSpec
                       {
                           format = SDL_PIXELFORMAT_RGBA32,
                           colorspace = SDL_Colorspace.SDL_COLORSPACE_SRGB,
                           width = mode.Width,
                           height = mode.Height,
                           framerate_numerator = mode.Fps,
                           framerate_denominator = 1,
                       };

        var camera = SDL_OpenCamera(cameraId, &spec);
        if (camera == null)
        {
            error = $"Could not open camera '{cameras[cameraIndex].Name}': {SDL_GetError()}";
            return null;
        }

        var stream = new Stream(this, camera, cameraId);
        _openStreams.Add(stream);
        return stream;
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
        public CameraStreamState State { get; private set; } = CameraStreamState.Open;

        public bool TryReadFrame(ref byte[] rgbaPixels, out int width, out int height)
        {
            width = 0;
            height = 0;

            lock (provider._sdlLock)
            {
                if (_isClosed)
                {
                    State = CameraStreamState.Disconnected;
                    return false;
                }

                UpdateState();
                if (State != CameraStreamState.Open)
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
                    for (var row = 0; row < height; row++)
                    {
                        Buffer.MemoryCopy(source + row * frame->pitch, destination + row * rowByteCount, rowByteCount, rowByteCount);
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
            if (_isClosed)
                return;

            _isClosed = true;
            SDL_CloseCamera(camera);
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
                State = CameraStreamState.Denied;
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
                State = CameraStreamState.Disconnected;
        }

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

    private readonly Lock _sdlLock = new();
    private readonly List<Stream> _openStreams = [];
    private bool _isStarted;
    private bool _isShutDown;
}
