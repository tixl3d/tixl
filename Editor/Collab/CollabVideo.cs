#nullable enable
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using T3.Core.DataTypes;
using T3.Core.Rendering.UploadPipeline;
using T3.Core.Resource;
using T3.Core.Utils;
using T3.Core.Video;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.Core.DataTypes.Vector;

namespace T3.Editor.Collab;

/// <summary>
/// Host side of the session preview: reads the main output texture back without stalling the render
/// thread, encodes it to H.264 on a worker, and hands each access unit to every member's video track.
/// </summary>
internal sealed class CollabVideoHostStreamer : IDisposable
{
    public string EncoderName => _encoderName;

    public bool IsEncoding => _encoder != null;

    /// <summary>Access units handed to the peer tracks, for status display and verification.</summary>
    public long EncodedFrames => Interlocked.Read(ref _encodedFrames);

    public int ViewerCount => _sink.ViewerCount;

    /// <summary>Cheap fingerprint of the last captured RGBA frame, for headless capture verification.</summary>
    public long FrameChecksum => Interlocked.Read(ref _frameChecksum);

    public CollabVideoHostStreamer(ICollabVideoSink sink, Func<T3.Core.DataTypes.Texture2D?>? source = null)
    {
        _sink = sink;
        _source = source;
        _readAccess = new TextureBgraReadAccess(useImmediateReadback: false, targetFormat: Format.R8G8B8A8_UNorm);

        _encodeThread = new Thread(EncodeLoop)
                            {
                                IsBackground = true,
                                Name = "CollabVideoEncode",
                            };
        _encodeThread.Start();
    }

    /// <summary>Render thread, once per frame. Pumps the deferred readback and schedules captures.</summary>
    public void Update()
    {
        _readAccess.Update();

        if (_disposed || _encoderFailed || _readbackInFlight)
            return;

        if (_sink.ViewerCount == 0)
            return;

        if (Environment.TickCount64 - _lastCaptureTicks < 1000 / TargetFps)
            return;

        // The shared feed is ONLY the explicit source (a selected input operator such as a webcam,
        // NDI or Spout input). Output windows are never streamed: everyone renders their own.
        var texture = _source != null ? _source() : RenderProcess.MainOutputTexture;
        if (Environment.TickCount64 - _lastDiagTicks > 3000)
        {
            _lastDiagTicks = Environment.TickCount64;
            Log.Debug($"Collab video: viewers={_sink.ViewerCount} customSource={_source != null} "
                      + $"texture={(texture == null ? "null" : $"{texture.Description.Width}x{texture.Description.Height}")}");
        }

        if (texture == null || texture.IsDisposed)
            return;

        _lastCaptureTicks = Environment.TickCount64;
        _readbackInFlight = _readAccess.InitiateConvertAndReadBack(texture, OnReadComplete);
    }

    /// <summary>Render thread, a few frames after <see cref="Update"/> scheduled the capture.</summary>
    private unsafe void OnReadComplete(TextureBgraReadAccess.ReadRequestItem request)
    {
        _readbackInFlight = false;
        var texture = request.CpuAccessTexture;
        if (texture == null || texture.IsDisposed)
            return;

        var width = texture.Description.Width;
        var height = texture.Description.Height;
        var rowBytes = width * 4;
        var size = rowBytes * height;

        var frame = TakeFreeBuffer(size);
        var context = ResourceManager.Device.ImmediateContext;
        var dataBox = context.MapSubresource(texture, 0, 0, MapMode.Read, MapFlags.None, out var imageStream);
        using var dataStream = imageStream;
        try
        {
            fixed (byte* destination = frame)
            {
                for (var y = 0; y < height; y++)
                {
                    var source = dataBox.DataPointer + y * dataBox.RowPitch;
                    System.Buffer.MemoryCopy((void*)source, destination + (long)y * rowBytes, rowBytes, rowBytes);
                }
            }
        }
        finally
        {
            context.UnmapSubresource(texture, 0);
        }

        Interlocked.Exchange(ref _frameChecksum, Checksum(frame, size));

        lock (_frameLock)
        {
            if (_pendingFrame != null)
                _freeBuffers.Push(_pendingFrame);

            _pendingFrame = frame;
            _pendingWidth = width;
            _pendingHeight = height;
        }

        _frameReady.Set();
    }

    private byte[] TakeFreeBuffer(int size)
    {
        lock (_frameLock)
        {
            while (_freeBuffers.Count > 0)
            {
                var candidate = _freeBuffers.Pop();
                if (candidate.Length >= size)
                    return candidate;
            }
        }

        return new byte[size];
    }

    /// <summary>The worker owns the encoder, so it is never freed while an encode is still running.</summary>
    private void EncodeLoop()
    {
        try
        {
            RunEncodeLoop();
        }
        finally
        {
            _encoder?.Dispose();
            _encoder = null;
        }
    }

    private void RunEncodeLoop()
    {
        while (!_disposed)
        {
            if (!_frameReady.WaitOne(100))
                continue;

            byte[]? frame;
            int width, height;
            lock (_frameLock)
            {
                frame = _pendingFrame;
                width = _pendingWidth;
                height = _pendingHeight;
                _pendingFrame = null;
            }

            if (frame == null)
                continue;

            if (_encoder == null && !TryCreateEncoder())
            {
                lock (_frameLock)
                {
                    _freeBuffers.Push(frame);
                }

                _encoderFailed = true;
                break;
            }

            var forceKeyFrame = _sink.ConsumeKeyFrameRequest()
                                || Environment.TickCount64 - _lastKeyFrameTicks > KeyFrameIntervalMs;
            if (forceKeyFrame)
                _lastKeyFrameTicks = Environment.TickCount64;

            try
            {
                _encoder!.EncodeScaled(frame, width * 4, width, height, forceKeyFrame);
            }
            catch (Exception e)
            {
                Log.Warning($"Collab: preview encode failed: {e.Message}");
            }

            lock (_frameLock)
            {
                _freeBuffers.Push(frame);
            }
        }
    }

    private bool TryCreateEncoder()
    {
        var factory = VideoExport.LiveFactory;
        if (factory == null)
        {
            // The video operator package owns FFmpeg and registers the factory; without it there is no encoder.
            Log.Warning("Collab: session preview needs the Video operator package (no H.264 encoder available)");
            return false;
        }

        var settings = new VideoLiveEncoderSettings
                           {
                               Width = TargetWidth,
                               Height = TargetHeight,
                               FrameRate = TargetFps,
                               BitRate = 3_000_000,
                           };

        var encoder = factory.TryCreateLiveEncoder(settings, out var error);
        if (encoder == null)
        {
            Log.Warning($"Collab: could not start the session preview encoder: {error}");
            return false;
        }

        encoder.AccessUnitEncoded += bytes =>
                                         {
                                             Interlocked.Increment(ref _encodedFrames);
                                             _sink.SendVideo(bytes);
                                         };
        _encoder = encoder;
        _encoderName = encoder.EncoderName;
        _lastKeyFrameTicks = 0;
        Log.Info($"Collab: session preview encoding with {_encoderName} ({TargetWidth}x{TargetHeight}@{TargetFps})");
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _frameReady.Set();
        _readAccess.Dispose();
    }

    private const int TargetFps = 30;
    private const int TargetWidth = 1280;
    private const int TargetHeight = 720;
    private const int KeyFrameIntervalMs = 2000;

    private readonly ICollabVideoSink _sink;
    private readonly Func<T3.Core.DataTypes.Texture2D?>? _source;
    private readonly TextureBgraReadAccess _readAccess;
    private readonly Thread _encodeThread;
    private readonly object _frameLock = new();
    private readonly Stack<byte[]> _freeBuffers = new();
    private readonly AutoResetEvent _frameReady = new(false);

    private ILiveVideoEncoder? _encoder;
    private string _encoderName = "";
    private byte[]? _pendingFrame;
    private int _pendingWidth;
    private int _pendingHeight;
    private long _lastCaptureTicks;
    private long _lastDiagTicks;
    private long _lastKeyFrameTicks;
    private long _encodedFrames;
    private long _frameChecksum;
    private bool _readbackInFlight;

    /** Set once the encoder could not be created, so capturing stops instead of feeding a dead worker. */
    private volatile bool _encoderFailed;

    internal static long Checksum(ReadOnlySpan<byte> pixels, int length)
    {
        var hash = 14695981039346656037UL;
        var count = Math.Min(length, pixels.Length);
        for (var i = 0; i < count; i += 64)
        {
            hash ^= pixels[i];
            hash *= 1099511628211UL;
        }

        return (long)hash;
    }
    private volatile bool _disposed;
}

/// <summary>
/// Reassembles H.264 access units from the RTP packets libdatachannel delivers on an incoming track.
/// Only the RFC 6184 packetization the libdatachannel sender emits is handled: single NAL, STAP-A and
/// FU-A. A frame is complete at the RTP marker bit.
/// </summary>
internal sealed class H264RtpDepacketizer
{
    /// <summary>Returns true when <paramref name="rtp"/> completed an access unit.</summary>
    public bool Push(ReadOnlySpan<byte> rtp, [NotNullWhen(true)] out byte[]? accessUnit)
    {
        accessUnit = null;
        if (rtp.Length < 12)
            return false;

        var headerLength = 12 + 4 * (rtp[0] & 0x0F);
        if ((rtp[0] & 0x10) != 0)
        {
            if (rtp.Length < headerLength + 4)
                return false;

            var extensionWords = (rtp[headerLength + 2] << 8) | rtp[headerLength + 3];
            headerLength += 4 + extensionWords * 4;
        }

        if (rtp.Length <= headerLength)
            return false;

        var marker = (rtp[1] & 0x80) != 0;
        var payload = rtp[headerLength..];
        var nalType = payload[0] & 0x1F;

        // Lost marker packets would otherwise glue frames together without end.
        if (_accessUnit.WrittenCount > MaxAccessUnitBytes)
        {
            _accessUnit.ResetWrittenCount();
            _fragmentOpen = false;
        }

        switch (nalType)
        {
            case >= 1 and <= 23:
                AppendNal(payload);
                break;

            case 24: // STAP-A
                var offset = 1;
                while (offset + 2 <= payload.Length)
                {
                    var size = (payload[offset] << 8) | payload[offset + 1];
                    offset += 2;
                    if (size <= 0 || offset + size > payload.Length)
                        break;

                    AppendNal(payload.Slice(offset, size));
                    offset += size;
                }

                break;

            case 28: // FU-A
                if (payload.Length < 2)
                    break;

                var fuHeader = payload[1];
                var start = (fuHeader & 0x80) != 0;

                if (start)
                {
                    WriteStartCode();
                    var nalHeader = (byte)((payload[0] & 0xE0) | (fuHeader & 0x1F));
                    _accessUnit.GetSpan(1)[0] = nalHeader;
                    _accessUnit.Advance(1);
                    _fragmentOpen = true;
                }

                // A lost start fragment leaves garbage in the buffer; the decoder drops it as an
                // undecodable packet and recovers at the next IDR.
                if (!_fragmentOpen)
                    break;

                _accessUnit.Write(payload[2..]);
                if ((fuHeader & 0x40) != 0)
                    _fragmentOpen = false;

                break;
        }

        if (!marker)
            return false;

        accessUnit = _accessUnit.WrittenSpan.ToArray();
        _accessUnit.ResetWrittenCount();
        _fragmentOpen = false;
        return accessUnit.Length > 0;
    }

    private void AppendNal(ReadOnlySpan<byte> nal)
    {
        WriteStartCode();
        _accessUnit.Write(nal);
    }

    private void WriteStartCode()
    {
        _accessUnit.Write<byte>([0, 0, 0, 1]);
    }

    /** Far above any 1280x720 access unit; past it the buffer holds frames whose marker was lost. */
    private const int MaxAccessUnitBytes = 4 * 1024 * 1024;

    private readonly ArrayBufferWriter<byte> _accessUnit = new(256 * 1024);
    private bool _fragmentOpen;
}

/// <summary>
/// Client side of the session preview: depacketizes the host's H.264 track, decodes on a worker, and
/// publishes the latest frame as a texture for the collab window.
/// </summary>
internal sealed class CollabVideoReceiver : IDisposable
{
    public CollabVideoReceiver(ICollabVideoSource source)
    {
        _source = source;
        _source.VideoRtpReceived += OnRtpReceived;

        _decodeThread = new Thread(DecodeLoop)
                            {
                                IsBackground = true,
                                Name = "CollabVideoDecode",
                            };
        _decodeThread.Start();
    }

    /// <summary>Decoded frames published to the display texture, for status display and verification.</summary>
    public long DecodedFrames => Interlocked.Read(ref _decodedFrames);

    public int Width => _frameWidth;
    public int Height => _frameHeight;

    /// <summary>Cheap fingerprint of the last decoded frame, for headless video-path verification.</summary>
    public long FrameChecksum => Interlocked.Read(ref _frameChecksum);

    /// <summary>Render thread: uploads a newly decoded frame to the display texture.</summary>
    public unsafe void Update()
    {
        int version;
        int width, height;
        byte[]? frame;
        lock (_frameLock)
        {
            version = _frameVersion;
            width = _frameWidth;
            height = _frameHeight;
            frame = _latestFrame;
        }

        if (frame == null || version == _uploadedVersion)
            return;

        EnsureTexture(width, height);

        // UpdateSubresource works on both backends: the Vulkan path routes it through a staging buffer,
        // while mapping a Dynamic texture (the operator-side upload) never reaches the image there.
        var context = ResourceManager.Device.ImmediateContext;
        fixed (byte* pixels = frame)
        {
            context.UpdateSubresource(_texture!, 0, null, (IntPtr)pixels, width * 4, width * 4 * height);
        }

        _uploadedVersion = version;
    }

    private void EnsureTexture(int width, int height)
    {
        if (_texture != null && !_texture.IsDisposed
                              && _texture.Description.Width == width
                              && _texture.Description.Height == height)
            return;

        DisposeTexture();

        var description = new Texture2DDescription
                              {
                                  BindFlags = BindFlags.ShaderResource,
                                  Format = Format.R8G8B8A8_UNorm,
                                  Width = width,
                                  Height = height,
                                  MipLevels = 1,
                                  SampleDescription = new SampleDescription(1, 0),
                                  Usage = ResourceUsage.Default,
                                  OptionFlags = ResourceOptionFlags.None,
                                  CpuAccessFlags = CpuAccessFlags.None,
                                  ArraySize = 1,
                              };

        _texture = T3.Core.DataTypes.Texture2D.CreateTexture2D(description);
        _view = SrvManager.GetSrvForTexture(_texture);
    }

    private void DisposeTexture()
    {
        _view = null;
        _texture?.Dispose();
        _texture = null;
        SrvManager.RemoveForDisposedTextures();
    }

    /// <summary>Render thread: the current preview texture, if a frame has been decoded.</summary>
    public bool TryGetTexture(out ShaderResourceView? view, out int width, out int height)
    {
        view = _view;
        width = _frameWidth;
        height = _frameHeight;
        return view != null && !view.IsDisposed && _texture is { IsDisposed: false } && _uploadedVersion != 0;
    }

    private void OnRtpReceived(byte[] rtp)
    {
        if (_depacketizer.Push(rtp, out var accessUnit))
            PublishAccessUnit(accessUnit);
    }

    private void PublishAccessUnit(byte[] accessUnit)
    {
        // Two frames in flight is enough for a preview; dropping the oldest bounds latency.
        while (_accessUnits.Count >= 2 && _accessUnits.TryTake(out _))
        {
        }

        try
        {
            _accessUnits.Add(accessUnit);
        }
        catch (InvalidOperationException)
        {
            // Receiver disposed.
        }
    }

    private void DecodeLoop()
    {
        try
        {
            foreach (var accessUnit in _accessUnits.GetConsumingEnumerable())
            {
                if (_decoder == null)
                {
                    var factory = VideoExport.LiveDecoderFactory;
                    if (factory == null)
                    {
                        Log.Warning("Collab: session preview needs the Video operator package (no H.264 decoder available)");
                        return;
                    }

                    _decoder = factory.TryCreateLiveVideoDecoder(out var error);
                    if (_decoder == null)
                    {
                        Log.Warning($"Collab: preview decoder unavailable: {error}");
                        return;
                    }
                }

                if (!_decoder.TryDecode(accessUnit))
                    continue;

                lock (_frameLock)
                {
                    var source = _decoder.Rgba;
                    if (_latestFrame == null || _latestFrame.Length < source.Length)
                        _latestFrame = new byte[source.Length];

                    source.CopyTo(_latestFrame);
                    _frameWidth = _decoder.Width;
                    _frameHeight = _decoder.Height;
                    _frameVersion++;
                    Interlocked.Increment(ref _decodedFrames);
                    Interlocked.Exchange(ref _frameChecksum, CollabVideoHostStreamer.Checksum(source, source.Length));
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Teardown.
        }
        finally
        {
            // The worker owns the decoder, so it is never freed while a decode is still running.
            _decoder?.Dispose();
            _decoder = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _source.VideoRtpReceived -= OnRtpReceived;
        _accessUnits.CompleteAdding();
        DisposeTexture();
    }

    private readonly ICollabVideoSource _source;
    private readonly H264RtpDepacketizer _depacketizer = new();
    private readonly BlockingCollection<byte[]> _accessUnits = new(new ConcurrentQueue<byte[]>());
    private readonly Thread? _decodeThread;
    private readonly object _frameLock = new();

    private ILiveVideoDecoder? _decoder;
    private byte[]? _latestFrame;
    private int _frameWidth;
    private int _frameHeight;
    private int _frameVersion;
    private int _uploadedVersion;
    private long _decodedFrames;
    private long _frameChecksum;
    private T3.Core.DataTypes.Texture2D? _texture;
    private ShaderResourceView? _view;
    private volatile bool _disposed;
}

/// <summary>Whoever can fan encoded access units out to peers that watch the shared feed.</summary>
internal interface ICollabVideoSink
{
    int ViewerCount { get; }
    void SendVideo(byte[] annexB);
    bool ConsumeKeyFrameRequest();
}

/// <summary>Whoever delivers the host's encoded video to this editor.</summary>
internal interface ICollabVideoSource
{
    event Action<byte[]>? VideoRtpReceived;
}
