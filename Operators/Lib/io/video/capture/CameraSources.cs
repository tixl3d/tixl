#nullable enable
using DirectShowLib;
using T3.SystemUi;

namespace Lib.io.video;

/// <summary>
/// Every camera [WebcamInput] can open: the application's camera provider (SDL — Media Foundation, V4L2,
/// AVFoundation) plus, on Windows, devices that exist only as DirectShow filters and are invisible to Media
/// Foundation (OBS Virtual Camera, DeckLink, NDI Webcam Input, older capture cards). Without an application
/// provider (a host that has no SDL), Windows still gets every DirectShow device.
/// </summary>
internal static class CameraSources
{
    public static ICameraProvider? Provider
    {
        get
        {
            var appProvider = Cameras.Provider;
            if (!OperatingSystem.IsWindows())
                return appProvider;

            // Reached from the evaluation thread and from every capture thread: without the lock two of them
            // can each build a provider, and the loser's DirectShow cache and refresh task are orphaned.
            lock (_providerLock)
            {
                if (_combined == null || !ReferenceEquals(_combined.AppProvider, appProvider))
                    _combined = new CombinedCameraProvider(appProvider);

                return _combined;
            }
        }
    }

    private static readonly object _providerLock = new();
    private static CombinedCameraProvider? _combined;

    private sealed class CombinedCameraProvider(ICameraProvider? appProvider) : ICameraProvider
    {
        public readonly ICameraProvider? AppProvider = appProvider;

        public void EnsureStarted() => AppProvider?.EnsureStarted();

        /// <summary>
        /// The app provider's cameras, then the DirectShow-only ones. DirectShow devices are listed from a cache
        /// refreshed in the background, because reading their modes binds each device (tens of milliseconds) and
        /// dropdowns call this every frame.
        /// </summary>
        public IReadOnlyList<CameraInfo> GetCameras()
        {
            var appCameras = AppProvider?.GetCameras() ?? [];
            RefreshDirectShowCacheIfStale();

            var directShowOnly = _directShowOnly;
            if (directShowOnly.Count == 0)
                return appCameras;

            var result = new List<CameraInfo>(appCameras.Count + directShowOnly.Count);
            result.AddRange(appCameras);
            foreach (var camera in directShowOnly)
            {
                if (!ContainsName(appCameras, camera.Name))
                    result.Add(camera);
            }

            return result;
        }

        public ICameraStream? TryOpen(string cameraName, CameraMode requestedMode, out string? error)
        {
            error = null;
            var appCameras = AppProvider?.GetCameras() ?? [];
            var isAppCamera = string.IsNullOrEmpty(cameraName) ? appCameras.Count > 0 : ContainsName(appCameras, cameraName);
            if (isAppCamera && AppProvider != null)
                return AppProvider.TryOpen(cameraName, requestedMode, out error);

            // Not an SDL camera: open it through DirectShow if such a filter exists. This runs on a capture
            // thread, so a fresh enumeration is fine here and finds devices connected since the last listing.
            DsDevice[] devices = [];
            try
            {
                devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
                foreach (var device in devices)
                {
                    if (!string.IsNullOrEmpty(cameraName) && !string.Equals(device.Name, cameraName, StringComparison.Ordinal))
                        continue;

                    if (ContainsName(appCameras, device.Name))
                        continue;

                    return DirectShowCameraStream.TryOpen(device, requestedMode, out error);
                }
            }
            catch (Exception e)
            {
                error = $"DirectShow device listing failed: {e.Message}";
                return null;
            }
            finally
            {
                foreach (var device in devices)
                    device.Dispose();
            }

            // Neither knows it: let the app provider explain (and look for newly connected cameras).
            if (AppProvider != null)
                return AppProvider.TryOpen(cameraName, requestedMode, out error);

            error = string.IsNullOrEmpty(cameraName) ? "No camera found." : $"Camera '{cameraName}' not found.";
            return null;
        }

        /** Called from the main thread (dropdowns) and from background mode reads, hence the lock. */
        private void RefreshDirectShowCacheIfStale()
        {
            lock (_refreshLock)
            {
                RefreshDirectShowCacheIfStaleLocked();
            }
        }

        private void RefreshDirectShowCacheIfStaleLocked()
        {
            var task = _refreshTask;
            if (task is { IsCompleted: true })
            {
                _refreshTask = null;
                if (task.IsCompletedSuccessfully)
                    _directShowOnly = task.Result;
            }

            var now = Environment.TickCount64;
            if (_refreshTask != null || now - _lastRefreshMs < DirectShowRefreshIntervalMs)
                return;

            _lastRefreshMs = now;
            var appProvider = AppProvider;
            _refreshTask = Task.Run(() => ReadDirectShowOnlyCameras(appProvider));
        }

        private static IReadOnlyList<CameraInfo> ReadDirectShowOnlyCameras(ICameraProvider? appProvider)
        {
            var appCameras = appProvider?.GetCameras() ?? [];
            var result = new List<CameraInfo>();
            DsDevice[] devices = [];
            try
            {
                devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
                foreach (var device in devices)
                {
                    if (string.IsNullOrEmpty(device.Name) || ContainsName(appCameras, device.Name) || ContainsName(result, device.Name))
                        continue;

                    var modes = DirectShowCameraStream.ReadModes(device);
                    modes.Sort(static (a, b) => (b.Width * b.Height).CompareTo(a.Width * a.Height));
                    result.Add(new CameraInfo(device.Name, modes.Distinct().ToArray()));
                }
            }
            catch (Exception e)
            {
                Log.Debug($"Listing DirectShow cameras failed: {e.Message}");
            }
            finally
            {
                foreach (var device in devices)
                    device.Dispose();
            }

            return result;
        }

        private static bool ContainsName(IReadOnlyList<CameraInfo> cameras, string name)
        {
            foreach (var camera in cameras)
            {
                if (string.Equals(camera.Name, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private const long DirectShowRefreshIntervalMs = 3000;

        private readonly object _refreshLock = new();
        private volatile IReadOnlyList<CameraInfo> _directShowOnly = [];
        private Task<IReadOnlyList<CameraInfo>>? _refreshTask;
        private long _lastRefreshMs = long.MinValue / 2;
    }
}
