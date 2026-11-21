#nullable enable
using libomtnet;
using T3.Core.Output.Streaming;
using T3.Core.Resource.Assets;
using T3.Core.Stats;
using T3.Graphics.Compat;

namespace Lib.Utils;

/// <summary>
/// One OMT sender. Used by the <c>OmtOutput</c> op and offered to the output setup through
/// <see cref="OmtStreamProvider"/>, so both paths share one implementation.
/// <para>The render thread only does GPU work: the frame is packed to UYVY (unless alpha is sent), copied into a ring
/// of staging textures, and the copy from two frames ago — finished by then — is read into native memory and handed
/// over. Encoding with the VMX codec costs several milliseconds, so it runs on the sender's own thread; at most two
/// frames wait there and the oldest is dropped, which keeps the stream live instead of falling behind.</para>
/// <para>Lifetime: each native <see cref="OMTSend"/> belongs to a <see cref="SendSession"/>, whose thread creates it
/// (binding sockets and announcing it over DNS-SD), sends with it and disposes it. Renaming or disposing only asks
/// the session to stop, so neither ever blocks the render loop, and nothing native is freed while a call on another
/// thread is still inside it.</para>
/// </summary>
internal sealed class OmtSender : IOutputStreamSender
{
    public OmtSender(string name)
    {
        _requestedName = SanitizeName(name);
        _lastRenameInput = name;
    }

    public string Name => _requestedName;

    public string? LastError => _lastError ?? _session?.Error;

    /// <summary>
    /// The name receivers discover, as "MACHINE (Source Name)"; null until the sender is announced. A user picks
    /// this in a receiver, so showing it is the only way to know what this machine advertises.
    /// </summary>
    public string? AdvertisedAddress => _session?.Address;

    /// <summary>Frames per second sent and advertised to receivers; rendered frames in between are skipped.</summary>
    public int FrameRate = 60;

    /// <summary>Whether the alpha channel is sent, or receivers get an opaque frame.</summary>
    public bool EnableAlpha;

    public void Configure(OutputStreamSettings settings)
    {
        FrameRate = settings.FrameRate;
        EnableAlpha = settings.EnableAlpha;
    }

    /// <summary>Re-targets the sender; takes effect on the next <see cref="Send"/>.</summary>
    public void Rename(string? name)
    {
        // Called every evaluation: only a changed name is worth sanitizing again.
        if (string.Equals(name, _lastRenameInput, StringComparison.Ordinal))
            return;

        _lastRenameInput = name;
        _requestedName = SanitizeName(name);
    }

    public bool Send(Texture2D frame)
    {
        try
        {
            return SendCore(frame);
        }
        catch (Exception e)
        {
            // An op's evaluation must never throw: report and keep the graph alive.
            _lastError = $"OMT send failed: {e.Message}";
            Log.Debug(e.ToString(), this);
            return false;
        }
    }

    public void Dispose()
    {
        _session?.RequestStop();
        _session = null;
        ReleasePipeline();
    }

    private bool SendCore(Texture2D frame)
    {
        if (!OmtRuntime.IsAvailable)
        {
            _lastError = OmtRuntime.MissingMessage;
            return false;
        }

        if (frame == null)
        {
            _lastError = "No texture";
            return false;
        }

        var description = frame.Description;
        var format = description.Format;
        if (!IsSupported(format))
        {
            _lastError = """
                         Texture format must be one of the following:
                         B8G8R8A8_UNorm
                         R8G8B8A8_UNorm
                         B8G8R8A8_Typeless
                         R8G8B8A8_Typeless

                         Please use [ConvertFormat] to convert the texture to a supported format.
                         """;
            return false;
        }

        if (description.SampleDescription.Count > 1)
        {
            _lastError = "Multisampled textures are not supported";
            return false;
        }

        if (description.ArraySize != 1)
        {
            _lastError = "Texture arrays are not supported";
            return false;
        }

        var width = description.Width;
        var height = description.Height;
        if (width < MinDimension || height < MinDimension)
        {
            _lastError = $"Frames must be at least {MinDimension}x{MinDimension} pixels (got {width}x{height})";
            return false;
        }

        _lastError = null;

        // A new name starts a new session; the old one announces its departure and closes on its own thread.
        if (_session == null || _session.Name != _requestedName || _session.IsFinished)
        {
            _session?.RequestStop();
            _session = SendSession.TryStart(_requestedName, out var startError);
            if (_session == null)
            {
                _lastError = $"Could not start the OMT sender: {startError}";
                return false;
            }
        }

        var session = _session;

        // Checked before any GPU work: with nobody watching, reading the frame back only to drop it is waste.
        if (session.Connections <= 0)
        {
            _queuedSlots.Clear();
            _nextSendTimeSec = 0;
            return true;
        }

        if (!IsDueAtFrameRate())
            return true;

        // UYVY carries no alpha, and a typeless frame can't be sampled for the conversion without knowing its format.
        var packsToUyvy = !EnableAlpha && format is Format.B8G8R8A8_UNorm or Format.R8G8B8A8_UNorm;

        var shapeChanged = _stagingTextures.Count > 0
                           && (width != _width || height != _height || packsToUyvy != _packsToUyvy || format != _sourceFormat);
        if (shapeChanged)
            ReleasePipeline();

        if (_stagingTextures.Count == 0 && !TryCreatePipeline(frame, width, height, packsToUyvy))
            return false;

        var context = ResourceManager.Device.ImmediateContext;
        Texture2D copySource = frame;
        if (packsToUyvy)
        {
            if (!TryPackToUyvy(frame))
                return false;

            copySource = _packedTexture!;
        }

        // At most LatencyInFrames copies wait at this point, so one of the SlotCount slots is always free.
        var freeSlot = FindFreeSlot();
        context.CopySubresourceRegion(copySource, 0, null, _stagingTextures[freeSlot], 0);
        _queuedSlots.Enqueue(freeSlot);

        // Read back the copy from two frames ago: by now the GPU has finished with it, so mapping doesn't stall.
        if (_queuedSlots.Count <= LatencyInFrames)
            return true;

        ReadBackAndQueue(session, _queuedSlots.Dequeue());
        return true;
    }

    private void ReadBackAndQueue(SendSession session, int slot)
    {
        var staging = _stagingTextures[slot];
        var context = ResourceManager.Device.ImmediateContext;
        var dataBox = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            if (dataBox.IsEmpty || dataBox.RowPitch <= 0)
                return;

            var rowBytes = _packsToUyvy ? _packedWidth * 4 : _width * 4;
            var stride = dataBox.RowPitch;
            if (stride < rowBytes)
                return;

            // The mapping only covers the last row's pixels, not its padding; the buffer still spans whole rows,
            // since that is what OMT reads.
            var length = stride * _height;
            var mappedLength = stride * (_height - 1) + rowBytes;
            var buffer = session.RentBuffer(length);
            if (buffer == null)
                return;

            unsafe
            {
                System.Buffer.MemoryCopy((void*)dataBox.DataPointer, (void*)buffer.Data, buffer.Capacity, mappedLength);
            }

            // The pack shader writes whole pixel pairs, so an odd width goes out one column wider.
            buffer.Width = _packsToUyvy ? _packedWidth * 2 : _width;
            buffer.Height = _height;
            buffer.Stride = stride;
            buffer.Length = length;
            buffer.Codec = _packsToUyvy ? OMTCodec.UYVY : OMTCodec.BGRA;
            buffer.HasAlpha = EnableAlpha;
            buffer.SwapRedAndBlue = !_packsToUyvy && _sourceFormat is Format.R8G8B8A8_UNorm or Format.R8G8B8A8_Typeless;
            buffer.FrameRate = FrameRate > 0 ? FrameRate : DefaultFrameRate;

            // OMT timestamps are 100 ns ticks; passing them keeps OMT from pacing (sleeping) on its own clock.
            buffer.Timestamp = (long)(FrameTiming.VisualFrameTimeSec * TimeSpan.TicksPerSecond);
            session.Enqueue(buffer);
        }
        finally
        {
            context.UnmapSubresource(staging, 0);
        }
    }

    private bool TryCreatePipeline(Texture2D frame, int width, int height, bool packsToUyvy)
    {
        _width = width;
        _height = height;
        _packsToUyvy = packsToUyvy;
        _sourceFormat = frame.Description.Format;
        _packedWidth = (width + 1) / 2;

        Texture2DDescription stagingDescription;
        if (packsToUyvy)
        {
            if (!TryLoadPackShader())
                return false;

            var packedDescription = new Texture2DDescription
                                        {
                                            Width = _packedWidth,
                                            Height = height,
                                            MipLevels = 1,
                                            ArraySize = 1,
                                            Format = Format.R8G8B8A8_UNorm,
                                            SampleDescription = new SampleDescription(1, 0),
                                            Usage = ResourceUsage.Default,
                                            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                            CpuAccessFlags = CpuAccessFlags.None,
                                            OptionFlags = ResourceOptionFlags.None,
                                        };
            _packedTexture = Texture2D.CreateTexture2D(packedDescription);
            _packedUav = new UnorderedAccessView(ResourceManager.Device, _packedTexture);
            stagingDescription = packedDescription;
        }
        else
        {
            stagingDescription = frame.Description;
            stagingDescription.MipLevels = 1;
        }

        stagingDescription.BindFlags = BindFlags.None;
        stagingDescription.CpuAccessFlags = CpuAccessFlags.Read;
        stagingDescription.Usage = ResourceUsage.Staging;
        stagingDescription.OptionFlags = ResourceOptionFlags.None;
        for (var i = 0; i < SlotCount; i++)
        {
            _stagingTextures.Add(Texture2D.CreateTexture2D(stagingDescription));
        }

        return true;
    }

    private bool TryLoadPackShader()
    {
        if (_packShaderResource?.Value != null)
            return true;

        if (!AssetRegistry.TryResolveAddress(PackShaderAddress, null, out var shaderPath, out _))
        {
            _lastError = $"Can't find {PackShaderAddress}";
            return false;
        }

        _packShaderResource = ResourceManager.CreateShaderResource<ComputeShader>(shaderPath, null, () => "main");
        if (_packShaderResource.Value != null)
            return true;

        _lastError = "The UYVY packing shader failed to compile";
        return false;
    }

    private bool TryPackToUyvy(Texture2D frame)
    {
        var shader = _packShaderResource?.Value;
        var sourceView = SrvManager.GetSrvForTexture(frame);
        if (shader == null || sourceView == null || _packedUav == null)
            return false;

        var context = ResourceManager.Device.ImmediateContext;
        var computeStage = context.ComputeShader;

        // Everything touched is put back: this runs inside whatever the host is rendering.
        context.PushState(StateGroups.ComputeShader | StateGroups.OutputMerger);

        // The frame is often still bound as a render target (the output compositor leaves its target bound), and
        // D3D11 silently drops a view onto a resource that is bound for writing — the shader would read black.
        context.OutputMerger.SetRenderTargets((DepthStencilView?)null, (RenderTargetView?)null);

        computeStage.Set(shader);
        computeStage.SetShaderResource(0, sourceView);
        computeStage.SetUnorderedAccessView(0, _packedUav);
        context.Dispatch((_packedWidth + 15) / 16, (_height + 15) / 16, 1);

        // Unbound before anything else touches the packed texture, which is read next as a copy source.
        context.PopState();

        return true;
    }

    /// <summary>
    /// Whether this frame is the one to send for the next 1/<see cref="FrameRate"/> step of the visual clock. The
    /// render loop runs at the display's rate, so a 30 fps stream from a 60 Hz loop takes every second frame.
    /// A frame is due when it is closer to the step than the next frame would be, so an exact multiple never
    /// flips on rounding.
    /// </summary>
    private bool IsDueAtFrameRate()
    {
        if (FrameRate <= 0)
            return true;

        var periodSec = 1.0 / FrameRate;
        var frameTimeSec = FrameTiming.VisualFrameTimeSec;
        var halfFrameSec = FrameTiming.VisualDeltaSec * 0.5;

        // After a stall (or the first frame) the schedule restarts from now instead of catching up in a burst.
        if (_nextSendTimeSec == 0 || frameTimeSec - _nextSendTimeSec > periodSec)
            _nextSendTimeSec = frameTimeSec;

        if (frameTimeSec + halfFrameSec < _nextSendTimeSec)
            return false;

        _nextSendTimeSec += periodSec;
        return true;
    }

    /** A slot not queued for readback; -1 only if the ring invariant above were broken. */
    private int FindFreeSlot()
    {
        for (var i = 0; i < _stagingTextures.Count; i++)
        {
            if (!_queuedSlots.Contains(i))
                return i;
        }

        return -1;
    }

    private void ReleasePipeline()
    {
        _queuedSlots.Clear();
        foreach (var stagingTexture in _stagingTextures)
            stagingTexture.Dispose();

        _stagingTextures.Clear();
        GraphicsUtilities.Dispose(ref _packedUav);
        GraphicsUtilities.Dispose(ref _packedTexture);
    }

    /// <summary>
    /// OMT derives the discoverable name from the machine name, so a name carrying the separator or a newline would
    /// make the source undiscoverable on the network.
    /// </summary>
    private static string SanitizeName(string? name)
    {
        var sanitized = OMTAddress.SanitizeName(name ?? string.Empty);
        return string.IsNullOrWhiteSpace(sanitized) ? DefaultName : sanitized.Trim();
    }

    private static bool IsSupported(Format format)
    {
        return format is Format.B8G8R8A8_UNorm or Format.R8G8B8A8_UNorm or Format.B8G8R8A8_Typeless or Format.R8G8B8A8_Typeless;
    }

    /// <summary>
    /// One announced source: the native sender, the thread that is its only user, and the frames waiting to be
    /// encoded. Never restarted; a rename gets a new session.
    /// </summary>
    private sealed class SendSession
    {
        private SendSession(string name)
        {
            Name = name;
        }

        public static SendSession? TryStart(string name, out string? error)
        {
            var session = new SendSession(name);
            try
            {
                var thread = new Thread(session.ThreadProc)
                                 {
                                     IsBackground = true,
                                     Name = "OmtSender",
                                     Priority = ThreadPriority.AboveNormal,
                                 };
                thread.Start();
                error = null;
                return session;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        public readonly string Name;

        public volatile string? Address;
        public volatile string? Error;

        /** Receivers connected; written by the session thread, so the render thread never asks the transport. */
        public volatile int Connections;

        /** The thread ended for good (couldn't open the sender); the owner starts a fresh session. */
        public bool IsFinished => _finished;

        /** Returns at once; the thread notices within one wait and cleans up after itself. */
        public void RequestStop()
        {
            _stopRequested = true;
            SignalFrameAvailable();
        }

        /// <summary>Memory for the next frame, reused from an encoded one when large enough; null once closed.</summary>
        public OmtFrameBuffer? RentBuffer(int length)
        {
            OmtFrameBuffer? buffer = null;
            lock (_lock)
            {
                if (_closed)
                    return null;

                if (_free.Count > 0)
                    buffer = _free.Pop();
            }

            if (buffer != null && buffer.Capacity >= length)
                return buffer;

            buffer?.Free();
            return OmtFrameBuffer.TryAllocate(length);
        }

        public void Enqueue(OmtFrameBuffer buffer)
        {
            lock (_lock)
            {
                if (_closed)
                {
                    buffer.Free();
                    return;
                }

                // Live video: a frame that waited behind two others is stale, so it is dropped rather than sent late.
                while (_queue.Count >= MaxQueuedFrames)
                    Recycle(_queue.Dequeue());

                _queue.Enqueue(buffer);
            }

            SignalFrameAvailable();
        }

        private void SignalFrameAvailable()
        {
            _frameAvailable.Set();
        }

        private void ThreadProc()
        {
            OMTSend? sender = null;
            try
            {
                try
                {
                    // Binds sockets and registers with DNS-SD, which is why it happens here and not on the render thread.
                    sender = new OMTSend(Name, OMTQuality.Default);
                    sender.SetSenderInformation(new OMTSenderInfo("TiXL", "TiXL", typeof(OmtSender).Assembly.GetName().Version?.ToString() ?? string.Empty));
                    Address = sender.Address;
                    Log.Debug($"OMT sender '{Address}' at {sender.URL}");
                }
                catch (Exception e)
                {
                    Error = e.Message;
                    Log.Warning($"Could not start the OMT sender '{Name}': {e.Message}");

                    // Retried by the owner with a fresh session after a pause, rather than once per frame.
                    WaitUntilStopped(RetryDelayMs);
                    return;
                }

                var lastConnections = -1;
                while (!_stopRequested)
                {
                    _frameAvailable.WaitOne(WaitTimeoutMs);

                    var connections = sender.Connections;
                    Connections = connections;
                    if (connections != lastConnections)
                    {
                        // Logged on change only, so the network path can be checked without a receiver-side tool.
                        Log.Debug($"OMT '{Address}': {connections} receiver(s)");
                        lastConnections = connections;
                    }

                    OmtFrameBuffer? buffer;
                    lock (_lock)
                    {
                        buffer = _queue.Count > 0 ? _queue.Dequeue() : null;
                    }

                    if (buffer == null)
                        continue;

                    try
                    {
                        SendFrame(sender, buffer);
                        Error = null;
                    }
                    catch (Exception e)
                    {
                        Error = $"OMT send failed: {e.Message}";
                        Log.Debug(e.ToString());
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            Recycle(buffer);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Last line of defence: an exception leaving a thread terminates the process.
                Error = e.Message;
                Log.Warning($"OMT sender '{Name}' stopped: {e.Message}");
            }
            finally
            {
                if (sender != null)
                {
                    try
                    {
                        sender.Dispose();
                    }
                    catch (Exception e)
                    {
                        Log.Debug($"Disposing the OMT sender threw: {e.Message}");
                    }
                }

                Connections = 0;
                Address = null;
                Close();
                _finished = true;
            }
        }

        private void WaitUntilStopped(int timeoutMs)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (!_stopRequested && Environment.TickCount64 < deadline)
                _frameAvailable.WaitOne(WaitTimeoutMs);
        }

        private static void SendFrame(OMTSend sender, OmtFrameBuffer buffer)
        {
            if (buffer.SwapRedAndBlue)
                SwapRedAndBlue(buffer);

            var frame = new OMTMediaFrame
                            {
                                Type = OMTFrameType.Video,
                                Codec = (int)buffer.Codec,
                                Width = buffer.Width,
                                Height = buffer.Height,
                                Stride = buffer.Stride,
                                FrameRateN = buffer.FrameRate,
                                FrameRateD = 1,
                                AspectRatio = (float)buffer.Width / buffer.Height,

                                // Matches the pack shader, which converts with BT.709 at every size.
                                ColorSpace = OMTColorSpace.BT709,
                                Flags = buffer.HasAlpha ? OMTVideoFlags.Alpha : OMTVideoFlags.None,
                                Timestamp = buffer.Timestamp,
                                Data = buffer.Data,
                                DataLength = buffer.Length,
                            };

            sender.Send(frame);
        }

        /// <summary>
        /// OMT has no RGBA codec; a raw RGBA frame would reach receivers with red and blue swapped. Done here rather
        /// than on the render thread, and as SIMD over whole pixels: keeping alpha and green fixed and rotating the
        /// other two bytes is the same swap.
        /// </summary>
        private static unsafe void SwapRedAndBlue(OmtFrameBuffer buffer)
        {
            var keepOddBytes = new Vector<uint>(0xFF00FF00u);
            var keepEvenBytes = new Vector<uint>(0x00FF00FFu);
            var rowPixels = buffer.Width;
            var data = (byte*)buffer.Data;

            for (var y = 0; y < buffer.Height; y++)
            {
                var row = new Span<uint>(data + (long)y * buffer.Stride, rowPixels);
                var x = 0;
                for (; x + Vector<uint>.Count <= row.Length; x += Vector<uint>.Count)
                {
                    var slice = row.Slice(x, Vector<uint>.Count);
                    var pixel = new Vector<uint>(slice);
                    var rotated = (pixel << 16) | (pixel >> 16);
                    ((pixel & keepOddBytes) | (rotated & keepEvenBytes)).CopyTo(slice);
                }

                for (; x < row.Length; x++)
                {
                    var pixel = row[x];
                    row[x] = (pixel & 0xFF00FF00u) | ((pixel << 16) & 0x00FF0000u) | ((pixel >> 16) & 0x000000FFu);
                }
            }
        }

        /** Under the lock. Keeps one buffer for reuse; more would only hold memory. */
        private void Recycle(OmtFrameBuffer buffer)
        {
            if (_closed || _free.Count >= MaxQueuedFrames + 1)
            {
                buffer.Free();
                return;
            }

            _free.Push(buffer);
        }

        private void Close()
        {
            lock (_lock)
            {
                _closed = true;
                while (_queue.Count > 0)
                    _queue.Dequeue().Free();

                while (_free.Count > 0)
                    _free.Pop().Free();
            }

        }

        private volatile bool _stopRequested;
        private volatile bool _finished;
        private readonly Lock _lock = new();
        private readonly Queue<OmtFrameBuffer> _queue = new();
        private readonly Stack<OmtFrameBuffer> _free = new();
        /** Never disposed: the render thread may still signal it after the thread ended. */
        private readonly AutoResetEvent _frameAvailable = new(false);
        private bool _closed;
    }

    /** Frames read back but not encoded yet; older ones are dropped, since a late frame is worse than a missing one. */
    private const int MaxQueuedFrames = 2;

    /** Copies between writing a staging texture and reading it: enough for the GPU to be done with it. */
    private const int LatencyInFrames = 2;

    /** Queued copies plus the one being written. */
    private const int SlotCount = LatencyInFrames + 1;

    /** VMX refuses anything smaller. */
    private const int MinDimension = 16;

    /** How often the session thread wakes without frames, to refresh the receiver count and notice a stop. */
    private const int WaitTimeoutMs = 100;

    private const int RetryDelayMs = 2000;

    private const string DefaultName = "TiXL";

    /** Advertised when no rate is set, where every rendered frame is sent. */
    private const int DefaultFrameRate = 60;
    private const string PackShaderAddress = "Lib:shaders/img/rgba-to-uyvy-cs.hlsl";

    private SendSession? _session;
    private string _requestedName;
    private string? _lastRenameInput;
    private string? _lastError;

    private int _width;
    private int _height;
    private int _packedWidth;
    private bool _packsToUyvy;
    private Format _sourceFormat;

    private Texture2D? _packedTexture;
    private UnorderedAccessView? _packedUav;
    private Resource<ComputeShader>? _packShaderResource;

    private readonly List<Texture2D> _stagingTextures = [];
    private readonly Queue<int> _queuedSlots = new();

    /** Visual-clock time the next frame is due at; 0 restarts the schedule. */
    private double _nextSendTimeSec;
}
