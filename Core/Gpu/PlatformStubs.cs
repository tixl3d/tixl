// Stubs for Windows-only library namespaces/types used by operator code.
// Allows compilation on Linux. These operators won't function but won't break the build.
#if !PLATFORM_WINDOWS

namespace DirectShowLib
{
    public class DsDevice
    {
        public string Name => "";
        public static DsDevice[] GetDevicesOfCat(System.Guid cat) => [];
    }

    public static class FilterCategory
    {
        public static readonly System.Guid VideoInputDevice = System.Guid.Empty;
    }

    public static class FormatType
    {
        public static readonly System.Guid VideoInfo = System.Guid.Empty;
        public static readonly System.Guid VideoInfo2 = System.Guid.Empty;
    }
}

namespace SharpDX.MediaFoundation
{
    public class MediaEngine : System.IDisposable
    {
        public void Dispose() { }
        public bool HasVideo() => false;
        public long Duration => 0;
        public long CurrentTime { get; set; }
        public bool IsPaused => true;
        public void Pause() { }
        public void Play() { }
        public void SetSource(string url) { }
        public void TransferVideoFrame(object texture, T3.Core.Gpu.VideoNormalizedRect? src, T3.Core.Gpu.RawRectangle dst, T3.Core.Gpu.RawColorBGRA? border) { }
    }

    public delegate void MediaEngineNotifyDelegate(MediaEngineEvent evt, long param1, int param2);
    public enum MediaEngineEvent { }
    public enum MediaEngineErr { }

    public class MediaEngineAttributes : System.Collections.Generic.Dictionary<System.Guid, object>
    {
        public void Set(System.Guid key, object value) => this[key] = value;
    }

    public class MediaEngineClassFactory : System.IDisposable
    {
        public void Dispose() { }
        public MediaEngine CreateInstance(int flags, MediaEngineAttributes attributes) => new();
    }

    public static class MediaEngineAttributeKeys
    {
        public static readonly System.Guid Callback = System.Guid.Empty;
        public static readonly System.Guid DxgiManager = System.Guid.Empty;
        public static readonly System.Guid VideoOutputFormat = System.Guid.Empty;
        public static readonly System.Guid Extension = System.Guid.Empty;
    }
}

namespace SharpDX.DXGI
{
    public class DXGIDeviceManager : System.IDisposable
    {
        public void Dispose() { }
        public void ResetDevice(object device) { }
    }
}

#endif
