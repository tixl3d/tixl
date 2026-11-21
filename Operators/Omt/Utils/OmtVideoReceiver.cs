#nullable enable
using libomtnet;
using T3.Graphics.Compat;

namespace Lib.Utils;

/// <summary>
/// Receives one OMT source and publishes its newest frame as a GPU texture.
/// <para>The render thread never calls into OMT and never waits: everything native about a connection lives in a
/// <see cref="ReceiveSession"/>, whose own thread creates the OMT receiver, receives with a short timeout, and
/// frees the receiver and its frame memory when it stops. <see cref="Connect"/> and <see cref="Dispose"/> only ask
/// a session to stop, so a receiver stuck on a dead network can never freeze the frame loop, and no native object
/// is freed while another thread is still inside it.</para>
/// <para>Frames are copied out of OMT's decode buffer (reused on the next receive) into native memory; one that the
/// render thread has not picked up yet is replaced rather than queued, so the texture always shows the newest
/// frame.</para>
/// </summary>
internal sealed class OmtVideoReceiver : IDisposable
{
    /// <summary>The texture holding the last uploaded frame; null until the first frame arrives.</summary>
    public Texture2D? Texture => _gpuTexture;

    public string? StatusMessage => _session?.StatusMessage ?? _staticStatusMessage;

    public bool IsConnected => _session?.IsConnected ?? false;

    public string? SourceName => _session?.SourceName;

    /// <summary>
    /// Starts receiving <paramref name="sourceName"/> (a discovered "MACHINE (Source)" name or an
    /// <c>omt://host:port</c> address). Returns at once; the previous stream winds down on its own thread.
    /// </summary>
    public void Connect(string? sourceName)
    {
        _session?.RequestStop();
        _session = null;

        if (!OmtRuntime.IsAvailable)
        {
            _staticStatusMessage = OmtRuntime.MissingMessage;
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            // No source means no picture; switching sources instead keeps the last frame until the new one arrives.
            _staticStatusMessage = null;
            GraphicsUtilities.Dispose(ref _gpuTexture);
            return;
        }

        var session = new ReceiveSession(sourceName.Trim());
        if (!session.TryStart(out var error))
        {
            _staticStatusMessage = $"Could not receive OMT source {sourceName}: {error}";
            Log.Warning(_staticStatusMessage, this);
            return;
        }

        _staticStatusMessage = null;
        _session = session;
    }

    /// <summary>
    /// Uploads the newest pending frame. Call on the render thread. Returns true when a new frame was uploaded.
    /// </summary>
    public bool Update()
    {
        var session = _session;
        var frame = session?.TakePending();
        if (frame == null)
            return false;

        try
        {
            return Upload(frame);
        }
        catch (Exception e)
        {
            _staticStatusMessage = $"Uploading an OMT frame failed: {e.Message}";
            Log.Debug(_staticStatusMessage, this);
            return false;
        }
        finally
        {
            session!.ReturnFrame(frame);
        }
    }

    /// <summary>Stops receiving and frees the texture. Call on the render thread, which owns the texture.</summary>
    public void Dispose()
    {
        _session?.RequestStop();
        _session = null;
        _staticStatusMessage = null;
        GraphicsUtilities.Dispose(ref _gpuTexture);
    }

    private bool Upload(OmtFrameBuffer frame)
    {
        if (_gpuTexture == null
            || _gpuTexture.Description.Width != frame.Width
            || _gpuTexture.Description.Height != frame.Height)
        {
            GraphicsUtilities.Dispose(ref _gpuTexture);

            // Device-local and filled with UpdateSubresource, like the camera, video and NDI operators: a
            // CPU-writable texture can't be sampled on Vulkan, where it is a plain upload buffer.
            _gpuTexture = Texture2D.CreateTexture2D(new Texture2DDescription
                                                        {
                                                            BindFlags = BindFlags.ShaderResource,
                                                            Format = Format.B8G8R8A8_UNorm,
                                                            Width = frame.Width,
                                                            Height = frame.Height,
                                                            MipLevels = 1,
                                                            SampleDescription = new SampleDescription(1, 0),
                                                            Usage = ResourceUsage.Default,
                                                            OptionFlags = ResourceOptionFlags.None,
                                                            CpuAccessFlags = CpuAccessFlags.None,
                                                            ArraySize = 1,
                                                        });
        }

        ResourceManager.Device.ImmediateContext.UpdateSubresource(new DataBox(frame.Data, frame.Stride, 0), _gpuTexture, 0);
        return true;
    }

    /// <summary>
    /// One stream: the OMT receiver, the thread that is its only user, and the frames handed to the render thread.
    /// A session is never restarted; a new source or a reconnect gets a new one, so a thread that outlives its
    /// stream can only ever touch its own state.
    /// </summary>
    private sealed class ReceiveSession
    {
        public ReceiveSession(string sourceName)
        {
            SourceName = sourceName;
            StatusMessage = $"Connecting to {sourceName}...";
        }

        public readonly string SourceName;

        public volatile string? StatusMessage;

        /** Written by the session thread on each wake-up, so the render thread never asks the transport. */
        public volatile bool IsConnected;

        public bool TryStart(out string? error)
        {
            try
            {
                var thread = new Thread(ThreadProc)
                                 {
                                     IsBackground = true,
                                     Name = "OmtReceiver",
                                 };
                thread.Start();
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /** Returns at once; the thread notices within one receive timeout and cleans up after itself. */
        public void RequestStop()
        {
            _stopRequested = true;
        }

        /// <summary>The newest received frame, or null when the render thread is already up to date.</summary>
        public OmtFrameBuffer? TakePending()
        {
            lock (_frameLock)
            {
                var frame = _pending;
                _pending = null;
                return frame;
            }
        }

        public void ReturnFrame(OmtFrameBuffer frame)
        {
            lock (_frameLock)
            {
                // The thread may have finished while the render thread was uploading; nobody else would free it.
                if (_closed || _spare != null)
                {
                    frame.Free();
                    return;
                }

                _spare = frame;
            }
        }

        private void ThreadProc()
        {
            OMTReceive? receiver = null;
            var consecutiveFailures = 0;
            var nextConnectAttempt = Environment.TickCount64;
            try
            {
                while (!_stopRequested)
                {
                    if (receiver == null)
                    {
                        // Creating a receiver can fail on a machine without networking; retried at a calm pace.
                        if (Environment.TickCount64 < nextConnectAttempt)
                        {
                            Thread.Sleep(ReceiveTimeoutMs);
                            continue;
                        }

                        nextConnectAttempt = Environment.TickCount64 + RetryDelayMs;
                        receiver = TryCreateReceiver();
                        continue;
                    }

                    try
                    {
                        var frame = new OMTMediaFrame();
                        if (!receiver.Receive(OMTFrameType.Video, ReceiveTimeoutMs, ref frame))
                        {
                            var connected = receiver.IsConnected();
                            IsConnected = connected;
                            StatusMessage = connected ? null : $"Waiting for {SourceName}...";
                            continue;
                        }

                        if (!TryStorePending(frame))
                            continue;

                        IsConnected = true;
                        StatusMessage = null;
                        consecutiveFailures = 0;
                    }
                    catch (Exception e)
                    {
                        if (_stopRequested)
                            break;

                        StatusMessage = $"OMT receive failed: {e.Message}";
                        Log.Debug($"OMT receive from {SourceName}: {e}");

                        // Failures that keep coming mean a wedged transport rather than a slow network: replace it
                        // instead of leaving a frozen frame behind forever.
                        if (++consecutiveFailures >= RebuildAfterConsecutiveFailures)
                        {
                            DisposeReceiver(ref receiver);
                            IsConnected = false;
                            consecutiveFailures = 0;
                        }

                        Thread.Sleep(ReceiveTimeoutMs);
                    }
                }
            }
            catch (Exception e)
            {
                // Last line of defence: an exception leaving a thread terminates the process.
                Log.Warning($"OMT receiver for {SourceName} stopped: {e.Message}");
            }
            finally
            {
                DisposeReceiver(ref receiver);
                IsConnected = false;
                CloseFrames();
            }
        }

        private OMTReceive? TryCreateReceiver()
        {
            try
            {
                var receiver = new OMTReceive(SourceName, OMTFrameType.Video, OMTPreferredVideoFormat.BGRA, OMTReceiveFlags.None);

                // Tells the sender it is live, as NdiInput does, so senders that show tally light up.
                receiver.SetTally(new OMTTally(0, 1));
                StatusMessage = $"Waiting for {SourceName}...";
                return receiver;
            }
            catch (Exception e)
            {
                StatusMessage = $"Could not receive {SourceName}: {e.Message}";
                Log.Debug(StatusMessage);
                return null;
            }
        }

        private static void DisposeReceiver(ref OMTReceive? receiver)
        {
            if (receiver == null)
                return;

            try
            {
                receiver.Dispose();
            }
            catch (Exception e)
            {
                Log.Debug($"Disposing an OMT receiver threw: {e.Message}");
            }

            receiver = null;
        }

        /// <summary>Copies a decoded frame out of OMT's buffer, which the next receive overwrites.</summary>
        private bool TryStorePending(in OMTMediaFrame frame)
        {
            if (frame.Data == IntPtr.Zero || frame.Codec != (int)OMTCodec.BGRA
                || frame.Width <= 0 || frame.Height <= 0 || frame.Width > MaxDimension || frame.Height > MaxDimension
                || frame.Stride < frame.Width * 4)
            {
                return false;
            }

            var length = (long)frame.Stride * frame.Height;
            if (length > frame.DataLength || length > int.MaxValue)
                return false;

            var buffer = TakeSpare((int)length);
            if (buffer == null)
                return false;

            unsafe
            {
                System.Buffer.MemoryCopy((void*)frame.Data, (void*)buffer.Data, buffer.Capacity, length);
            }

            buffer.Width = frame.Width;
            buffer.Height = frame.Height;
            buffer.Stride = frame.Stride;

            lock (_frameLock)
            {
                if (_closed)
                {
                    buffer.Free();
                    return false;
                }

                // Not picked up yet: the newer frame replaces it.
                if (_pending != null)
                {
                    if (_spare == null)
                        _spare = _pending;
                    else
                        _pending.Free();
                }

                _pending = buffer;
            }

            return true;
        }

        private OmtFrameBuffer? TakeSpare(int length)
        {
            OmtFrameBuffer? spare;
            lock (_frameLock)
            {
                spare = _spare;
                _spare = null;
            }

            if (spare != null && spare.Capacity >= length)
                return spare;

            spare?.Free();
            return OmtFrameBuffer.TryAllocate(length);
        }

        /** Called by the session thread once it no longer fills frames; one the render thread holds is freed on return. */
        private void CloseFrames()
        {
            lock (_frameLock)
            {
                _closed = true;
                _pending?.Free();
                _pending = null;
                _spare?.Free();
                _spare = null;
            }
        }

        private volatile bool _stopRequested;
        private readonly Lock _frameLock = new();
        private OmtFrameBuffer? _pending;
        private OmtFrameBuffer? _spare;
        private bool _closed;
    }

    /** Short enough that a stop request is noticed quickly, long enough not to spin. */
    private const int ReceiveTimeoutMs = 100;

    private const int RetryDelayMs = 2000;

    /** Roughly a second of back-to-back failures before the transport is rebuilt. */
    private const int RebuildAfterConsecutiveFailures = 10;

    /** Larger frames are a corrupt header, not video: D3D11's texture limit. */
    private const int MaxDimension = 16384;

    private ReceiveSession? _session;
    private Texture2D? _gpuTexture;

    /** Status while no session exists (codec missing, start failed); a running stream reports through its session. */
    private string? _staticStatusMessage;
}
