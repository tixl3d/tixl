#nullable enable
using System.Threading;
using Sdcb.FFmpeg.Utils;
using T3.Core.Video;
using T3.Graphics.Compat;
using T3.VideoServices;

namespace Lib.io.video;

/// <summary>
/// Lets the other TiXL clients of a session use this machine's cameras: the editor asks for a camera by name,
/// opens it through this provider and streams it. Discovered by type when the package loads.
/// </summary>
internal sealed class PeerCameraProvider : IPeerCameraProvider
{
    public IReadOnlyList<string> GetCameraNames()
    {
        WebcamScan.Scan();
        var names = new List<string>();
        foreach (var device in WebcamScan.Devices)
            names.Add(device.Name);

        return names;
    }

    public IPeerCameraCapture? Open(string cameraName, out string? error)
    {
        // A camera an operator here already has open cannot be opened twice (dshow, V4L2 are exclusive), but its
        // frames are right there: share those instead.
        foreach (var input in RemoteVideoInputBus.Inputs)
        {
            if (string.Equals(input.RelayDisplayName, cameraName, StringComparison.Ordinal) && input.RelayTexture != null)
            {
                error = null;
                return new SharedOperatorCapture(input);
            }
        }

        WebcamScan.Scan();
        WebcamWithIndex? device = null;
        foreach (var candidate in WebcamScan.Devices)
        {
            if (candidate.Name == cameraName)
            {
                device = candidate;
                break;
            }
        }

        if (device == null)
        {
            error = $"the camera '{cameraName}' is not connected to this machine";
            return null;
        }

        error = null;
        return new HeadlessCameraCapture(device);
    }

    private sealed class SharedOperatorCapture(IRemoteVideoInput input) : IPeerCameraCapture
    {
        public Texture2D? Update()
        {
            foreach (var live in RemoteVideoInputBus.Inputs)
            {
                if (ReferenceEquals(live, input))
                    return input.RelayTexture;
            }

            return null;
        }

        public string Status => "sharing the camera of a running video input";

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Captures one camera on its own thread, without an operator, at the device's default format. The newest frame
/// is uploaded to a texture when the editor asks for it; frames in between are dropped, which is what a
/// network stream wants.
/// </summary>
internal sealed class HeadlessCameraCapture : IPeerCameraCapture
{
    public HeadlessCameraCapture(WebcamWithIndex device)
    {
        _device = device;
        _thread = new Thread(CaptureLoop)
                      {
                          IsBackground = true,
                          Name = "Peer Camera Capture",
                          Priority = ThreadPriority.BelowNormal,
                      };
        _thread.Start();
    }

    public string Status => _status;

    public unsafe Texture2D? Update()
    {
        int width;
        int height;
        lock (_lock)
        {
            if (!_hasNewFrame || _frame == null)
                return null;

            _hasNewFrame = false;
            width = _width;
            height = _height;

            if (_texture == null || _textureWidth != width || _textureHeight != height)
            {
                GraphicsUtilities.Dispose(ref _texture);
                _texture = Texture2D.CreateTexture2D(new Texture2DDescription
                                                         {
                                                             Width = width,
                                                             Height = height,
                                                             MipLevels = 1,
                                                             ArraySize = 1,
                                                             Format = Format.R8G8B8A8_UNorm,
                                                             SampleDescription = new SampleDescription(1, 0),
                                                             Usage = ResourceUsage.Default,
                                                             BindFlags = BindFlags.ShaderResource,
                                                             CpuAccessFlags = CpuAccessFlags.None,
                                                             OptionFlags = ResourceOptionFlags.None,
                                                         });
                _textureWidth = width;
                _textureHeight = height;
            }

            fixed (byte* pixels = _frame)
            {
                var dataBox = new DataBox((IntPtr)pixels, width * 4, 0);
                ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _texture, 0);
            }
        }

        return _texture;
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
            Log.Debug($"Peer camera '{_device.Name}': the capture thread did not exit in time; it finishes on its own.");

        lock (_lock)
        {
            GraphicsUtilities.Dispose(ref _texture);
        }
    }

    private void CaptureLoop()
    {
        var token = _cancellation.Token;
        while (!token.IsCancellationRequested)
        {
            CaptureOnce(token);

            // The device never opened or died (unplugged, busy); retry so a restored camera comes back by itself.
            for (var waited = 0; waited < 1000 && !token.IsCancellationRequested; waited += 100)
                Thread.Sleep(100);
        }
    }

    private unsafe void CaptureOnce(CancellationToken token)
    {
        VideoDecoderSession? session = null;
        SoftwareFrameConverter? converter = null;
        try
        {
            var url = VideoDeviceInput.BuildCaptureUrl(_device, out var inputFormat);
            session = VideoDecoderSession.TryOpen(url, VideoPlaybackOptimization.FastSeeking, out var error,
                                                  VideoDeviceInput.BuildDefaultDemuxerOptions(), inputFormat, lowLatency: true);
            if (session == null)
            {
                _status = error != null && (error.Contains("-16") || error.Contains("busy", StringComparison.OrdinalIgnoreCase))
                              ? $"the camera '{_device.Name}' is in use by another application"
                              : $"could not open '{_device.Name}': {error}";
                return;
            }

            converter = new SoftwareFrameConverter(hdr: false);
            while (!token.IsCancellationRequested)
            {
                if (!session.TryReadNextFrame(out _))
                {
                    _status = $"lost the camera '{_device.Name}'";
                    return;
                }

                var rgba = converter.Convert(session.CurrentFrame, session.Width, session.Height);
                var rowBytes = rgba.Width * converter.BytesPerPixel;
                var total = rowBytes * rgba.Height;
                var stride = rgba.Linesize[0];
                var source = (byte*)rgba.Data[0];

                lock (_lock)
                {
                    if (_frame == null || _frame.Length < total)
                        _frame = new byte[total];

                    fixed (byte* target = _frame)
                    {
                        if (stride == rowBytes)
                        {
                            System.Buffer.MemoryCopy(source, target, total, total);
                        }
                        else
                        {
                            for (var y = 0; y < rgba.Height; y++)
                                System.Buffer.MemoryCopy(source + (long)y * stride, target + (long)y * rowBytes, rowBytes, rowBytes);
                        }
                    }

                    _width = rgba.Width;
                    _height = rgba.Height;
                    _hasNewFrame = true;
                }

                _status = "live";
            }
        }
        catch (Exception e)
        {
            _status = $"capture failed: {e.Message}";
        }
        finally
        {
            converter?.Dispose();
            session?.Dispose();
        }
    }

    private readonly WebcamWithIndex _device;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _lock = new();
    private byte[]? _frame;
    private int _width;
    private int _height;
    private bool _hasNewFrame;
    private Texture2D? _texture;
    private int _textureWidth;
    private int _textureHeight;
    private volatile string _status = "starting";
}
