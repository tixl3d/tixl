#nullable enable
using System.Threading;
using T3.Core.Utils;
using T3.Core.Video;
using T3.Graphics.Compat;
using T3.VideoServices;

namespace Lib.io.video
{
    /// <summary>
    /// Pulls a network video stream (RTSP, HTTP/MJPEG, RTMP, HLS, ...) or hosts a small RTMP server, and
    /// exposes the newest decoded frame as a texture.
    /// </summary>
    /// <remarks>
    /// All network I/O and decoding happen on a background capture thread; evaluation never blocks on it.
    /// Each connection attempt is a <see cref="CaptureRun"/> that owns its FFmpeg session outright, so stopping
    /// is just a cancel: the run tears itself down, and FFmpeg's <c>rw_timeout</c> bounds any single blocking
    /// read, so a source that stops sending cannot pin the thread. Runs are tagged with an id, and frames or
    /// status from a run that is no longer active are dropped, so a slow-to-exit run can never overwrite a
    /// newer one.
    /// </remarks>
    [Guid("D9A7233D-5D03-4268-A58B-465972852A5B")]
    internal sealed class VideoStreamInput : Instance<VideoStreamInput>, IStatusProvider
    {
        [Input(Guid = "2E26E552-68D7-4E2F-8208-831F2A75C96B")]
        public readonly InputSlot<bool> Connect = new();

        [Input(Guid = "9A240243-71B5-4235-86A9-D5369A3311A9")]
        public readonly InputSlot<bool> Reconnect = new();

        [Output(Guid = "2E7E2404-5881-4327-9653-CA9533B856A9", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Texture2D?> Texture = new();

        [Output(Guid = "3F6A960C-906A-4073-A338-ABB785869062", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Int2> Resolution = new();

        [Output(Guid = "B0E4313B-A746-4444-934E-14285D42DADB", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public new readonly Slot<string> Status = new();

        [Input(Guid = "A8B0971B-94CB-4C3D-932D-337581B8D83A")]
        public readonly InputSlot<string> Url = new("rtsp://your_stream_url");

        [Input(Guid = "E268149F-7EB7-48E3-B4B5-39A1DE9CA075")]
        public readonly InputSlot<bool> RtmpServer = new();

        [Input(Guid = "A6AF67EA-2103-4AB5-8AC9-7C269CD25D14")]
        public readonly InputSlot<int> RtmpPort = new(1935);

        // Frame handoff bookkeeping: the capture thread bumps the counter under the lock; the eval thread
        // uploads only when it sees a counter it has not uploaded yet.
        private int _sharedFrameCounter;
        private int _uploadedFrameCounter;
        private byte[]? _uploadBuffer;

        public VideoStreamInput()
        {
            // Every output drives the update, so a graph that only reads Status or Resolution still connects.
            Texture.UpdateAction = Update;
            Resolution.UpdateAction = Update;
            Status.UpdateAction = Update;
        }

        public IStatusProvider.StatusLevel GetStatusLevel() => _status.Level;

        public string GetStatusMessage() => _status.Message;

        private void Update(EvaluationContext context)
        {
            var url = (Url.GetValue(context) ?? string.Empty).Trim();
            var shouldConnect = Connect.GetValue(context);
            var useRtmpServer = RtmpServer.GetValue(context);
            var rtmpPort = Math.Clamp(RtmpPort.GetValue(context), 1, 65535);
            var reconnect = Reconnect.GetValue(context);
            if (reconnect)
                Reconnect.SetTypedInputValue(false);

            UpdateCaptureState(url, shouldConnect, useRtmpServer, rtmpPort, reconnect);
            UpdateOutputs();
        }

        private void UpdateCaptureState(string url, bool shouldConnect, bool useRtmpServer, int rtmpPort, bool reconnect)
        {
            // Pulling a URL and hosting the RTMP server are mutually exclusive; the server toggle wins.
            // Connect/Url changes are ignored in server mode — restarting the listener over a toggle that
            // has no effect would disconnect a publisher for nothing.
            var pullSettingsChanged = shouldConnect != _lastConnectState || (shouldConnect && url != _lastUrl);
            var serverSettingsChanged = useRtmpServer != _lastRtmpServerState
                                        || (useRtmpServer && rtmpPort != _lastRtmpPort);
            var settingsChanged = serverSettingsChanged || (!useRtmpServer && pullSettingsChanged);

            _lastConnectState = shouldConnect;
            _lastRtmpServerState = useRtmpServer;

            if (!useRtmpServer && !shouldConnect)
            {
                StopCapture("Not connected.");
                return;
            }

            if (!useRtmpServer && string.IsNullOrEmpty(url))
            {
                StopCapture("Warning: No stream URL set.");
                _lastUrl = url;
                return;
            }

            if (settingsChanged || reconnect)
            {
                // Kept until the start happens: it may have to wait for a previous run to release its port.
                _restartRequested = true;
                if (_activeRun != null)
                    StopCapture("Reconnecting...");
            }

            // A run that ended on its own (open refused, stream ended) is replaced after a pause: an immediate
            // restart would reconnect every frame and overwrite the error status before anyone sees it.
            if (_activeRun is { HasExited: true })
                RetireActiveRun();

            if (_activeRun != null)
                return;

            var mayStart = _restartRequested || Environment.TickCount64 - _lastRunStartMs >= AutoReconnectDelayMs;
            if (!mayStart || !HaveRetiredRunsExited())
                return;

            _lastUrl = url;
            _lastRtmpPort = rtmpPort;
            _restartRequested = false;
            StartCapture(useRtmpServer ? null : url, rtmpPort);
        }

        /// <summary>Uploads the newest frame (if any arrived since the last evaluation) and refreshes the outputs.</summary>
        private void UpdateOutputs()
        {
            // A stream delivers ~25-30 fps while the editor evaluates ~60 — uploading only on a new frame avoids
            // sending each frame twice. The handoff is a buffer swap, so the lock is never held during a copy.
            bool hasNewFrame;
            bool frameAvailable;
            lock (_frameLock)
            {
                hasNewFrame = _hasPendingFrame;
                if (hasNewFrame)
                {
                    (_pendingFrame, _uploadFrame) = (_uploadFrame, _pendingFrame);
                    _hasPendingFrame = false;
                }

                frameAvailable = _frameAvailable;
            }

            if (hasNewFrame)
            {
                UploadFrameToGpu(_uploadFrame);
                Resolution.Value = new Int2(_uploadFrame.Width, _uploadFrame.Height);
            }

            Texture.Value = frameAvailable ? _gpuTexture : null;
            Status.Value = _status.Message;
        }

        private void StartCapture(string? pullUrl, int rtmpPort)
        {
            var run = new CaptureRun(++_lastRunId);
            lock (_frameLock)
            {
                _activeRunId = run.Id;
                _frameAvailable = false;
                _hasPendingFrame = false;
            }

            var isServer = pullUrl == null;
            run.Thread = new Thread(() => RunCapture(run, pullUrl, rtmpPort))
                             {
                                 IsBackground = true,
                                 Name = isServer ? "VideoStreamInput RTMP Server" : "VideoStreamInput Capture",
                             };

            _activeRun = run;
            _lastRunStartMs = Environment.TickCount64;
            run.Thread.Start();
        }

        /// <summary>
        /// Cancels the active run without waiting for it; its frames and status are ignored from here on. The next
        /// run starts once this one has exited (see <see cref="HaveRetiredRunsExited"/>), which takes until the
        /// frame read already in flight returns — bounded by the stream's I/O timeout.
        /// </summary>
        private void StopCapture(string statusMessage)
        {
            if (_activeRun == null)
            {
                if (_status.Message != statusMessage)
                    SetOwnStatus(statusMessage);
                return;
            }

            _activeRun.Cancellation.Cancel();
            RetireActiveRun();

            lock (_frameLock)
            {
                _activeRunId = NoRunId;
                _frameAvailable = false;
                _hasPendingFrame = false;
            }

            SetOwnStatus(statusMessage);
        }

        private void RetireActiveRun()
        {
            var run = _activeRun!;
            _activeRun = null;
            if (run.HasExited)
                return;

            run.RetiredAtMs = Environment.TickCount64;
            _retiredRuns.Add(run);
        }

        /// <summary>
        /// False while a cancelled run is still shutting down, which keeps two runs from fighting over one RTMP
        /// port or camera connection. A run that ignores cancellation (e.g. stuck in a DNS lookup, which FFmpeg
        /// cannot interrupt) is abandoned after <see cref="AbandonStuckRunAfterMs"/>; it owns all of its
        /// resources and releases them whenever it does return.
        /// </summary>
        private bool HaveRetiredRunsExited()
        {
            var now = Environment.TickCount64;
            var allExited = true;
            for (var runIndex = _retiredRuns.Count - 1; runIndex >= 0; runIndex--)
            {
                var run = _retiredRuns[runIndex];
                if (run.HasExited)
                {
                    _retiredRuns.RemoveAt(runIndex);
                    continue;
                }

                if (now - run.RetiredAtMs > AbandonStuckRunAfterMs)
                {
                    Log.Warning($"VideoStreamInput: a stopped capture thread has not exited after {AbandonStuckRunAfterMs / 1000} s; abandoning it.", this);
                    _retiredRuns.RemoveAt(runIndex);
                    continue;
                }

                allExited = false;
            }

            if (!allExited)
                SetOwnStatus("Waiting for the previous connection to close...");

            return allExited;
        }

        /// <summary>Capture-thread entry point. Nothing may escape it: an unhandled exception on a thread ends the process.</summary>
        private void RunCapture(CaptureRun run, string? pullUrl, int rtmpPort)
        {
            try
            {
                if (pullUrl == null)
                {
                    RunRtmpServer(run, rtmpPort);
                }
                else
                {
                    PullStream(run, pullUrl);
                }
            }
            catch (Exception e)
            {
                if (!run.Token.IsCancellationRequested)
                    ReportFailure(run, $"Error: Capture thread failed: {e.Message}");
            }
            finally
            {
                lock (_frameLock)
                {
                    if (run.Id == _activeRunId)
                    {
                        _frameAvailable = false;
                        _hasPendingFrame = false;
                    }
                }

                run.HasExited = true;
            }
        }

        private void PullStream(CaptureRun run, string url)
        {
            SetRunStatus(run, IStatusProvider.StatusLevel.Notice, $"Connecting to {url}...");

            // A live stream is consumed sequentially through this op's own SoftwareFrameConverter, so it needs
            // CPU-side frames — FastSeeking decodes in software, which provides exactly that.
            using var session = VideoDecoderSession.TryOpen(url, VideoPlaybackOptimization.FastSeeking, out var error,
                                                            BuildDemuxerOptions(url),
                                                            cancellationToken: run.Token,
                                                            ioTimeout: StreamStallTimeout);
            if (run.Token.IsCancellationRequested)
                return;

            if (session == null)
            {
                ReportFailure(run, $"Error: Failed to open stream '{url}'. {error}");
                return;
            }

            SetRunStatus(run, IStatusProvider.StatusLevel.Success, "Streaming");
            _lastLoggedFailure = null;

            if (DecodeUntilStreamEnds(run, session))
                ReportFailure(run, "Warning: Stream ended. Reconnecting...");
        }

        /// <summary>
        /// Hosts a minimal RTMP server: FFmpeg's RTMP protocol can listen for a publisher (OBS, a phone streaming
        /// app, ffmpeg) and demux the pushed FLV stream. Each listen attempt waits a short, finite time so the
        /// advertised LAN address stays current; a publisher that disconnects puts the operator back into
        /// listening mode instead of ending the run.
        /// </summary>
        private void RunRtmpServer(CaptureRun run, int port)
        {
            var listenUrl = $"rtmp://0.0.0.0:{port}/{RtmpListenPath}";

            // `rtmp_listen` is the RTMP protocol's own option (the CLI's `-listen` flag maps onto the nested
            // TCP protocol and is NOT what avformat looks up here). `timeout` is in SECONDS for RTMP (RTSP
            // uses microseconds) and implies listen mode. Never pass either on a pull URL — it would turn
            // that open into a server that waits for a publisher instead of connecting.
            var options = new Dictionary<string, string>
                              {
                                  ["rtmp_listen"] = "1",
                                  ["timeout"] = RtmpListenTimeoutSec.ToString(),
                                  ["rtmp_live"] = "live",
                                  ["rtmp_buffer"] = RtmpClientBufferMs,
                              };

            while (!run.Token.IsCancellationRequested)
            {
                var publicUrl = BuildRtmpServerUrl(port);
                SetRunStatus(run, IStatusProvider.StatusLevel.Notice, $"RTMP server listening on {publicUrl} — waiting for a publisher...");

                var attemptStartedMs = Environment.TickCount64;
                using (var session = VideoDecoderSession.TryOpen(listenUrl, VideoPlaybackOptimization.FastSeeking, out var error,
                                                                 options,
                                                                 cancellationToken: run.Token,
                                                                 ioTimeout: StreamStallTimeout))
                {
                    if (run.Token.IsCancellationRequested)
                        return;

                    if (session == null)
                    {
                        // The expected failure is "nobody connected within the timeout", which takes the full
                        // wait. Only an immediate failure (port in use, protocol missing) is worth reporting
                        // and backing off from; otherwise reopen at once to keep the listen window continuous.
                        if (Environment.TickCount64 - attemptStartedMs < ImmediateListenFailureMs)
                        {
                            ReportFailure(run, $"Error: RTMP server could not listen on port {port}. {error}");
                            if (WaitOrCancelled(run.Token, ListenRetryDelayMs))
                                return;
                        }

                        continue;
                    }

                    SetRunStatus(run, IStatusProvider.StatusLevel.Success, $"Streaming (RTMP) — publisher connected to {publicUrl}");
                    _lastLoggedFailure = null;

                    if (DecodeUntilStreamEnds(run, session))
                        SetRunStatus(run, IStatusProvider.StatusLevel.Warning, "Warning: RTMP publisher disconnected.");
                }

                // Give a publisher that is reconnecting a moment before the port is reopened.
                if (WaitOrCancelled(run.Token, PublisherReconnectGraceMs))
                    return;
            }
        }

        /// <summary>
        /// Decodes and publishes frames until the run is cancelled, the stream fails (reported here, including a
        /// stall past <see cref="StreamStallTimeout"/>), or the source ends the stream — the only case that
        /// returns true, leaving the caller to say what an ended stream means for it.
        /// </summary>
        private bool DecodeUntilStreamEnds(CaptureRun run, VideoDecoderSession session)
        {
            // The hand-off buffers and the upload texture are RGBA8; an HDR converter's 8-byte pixels would be
            // copied at half length and read as garbage.
            using var converter = new SoftwareFrameConverter(hdr: false);
            var writeFrame = new RgbaFrame();
            try
            {
                while (!run.Token.IsCancellationRequested)
                {
                    if (!session.TryReadNextFrame(out _))
                        return !run.Token.IsCancellationRequested;

                    var rgba = converter.Convert(session.CurrentFrame);
                    if (rgba.Width <= 0 || rgba.Height <= 0)
                        continue;

                    writeFrame.EnsureCapacity(rgba.Width, rgba.Height);
                    converter.CopyPixels(rgba, writeFrame.Pixels);
                    PublishFrame(run, ref writeFrame);
                }

                return false;
            }
            catch (Exception e) when (!run.Token.IsCancellationRequested)
            {
                ReportFailure(run, $"Warning: Stream interrupted: {e.Message} Reconnecting...");
                return false;
            }
        }

        /// <summary>Hands a fully written frame to the eval thread by swapping it with the pending slot.</summary>
        private void PublishFrame(CaptureRun run, ref RgbaFrame writeFrame)
        {
            lock (_frameLock)
            {
                if (run.Id != _activeRunId)
                    return;

                (writeFrame, _pendingFrame) = (_pendingFrame, writeFrame);
                _hasPendingFrame = true;
                _frameAvailable = true;
            }
        }

        private void SetRunStatus(CaptureRun run, IStatusProvider.StatusLevel level, string message)
        {
            lock (_frameLock)
            {
                if (run.Id != _activeRunId)
                    return;

                _status = new StatusSnapshot(level, message);
            }
        }

        /// <summary>Sets a failure status and logs it once — automatic reconnects repeat the same failure every few seconds.</summary>
        private void ReportFailure(CaptureRun run, string message)
        {
            var level = message.StartsWith("Error", StringComparison.Ordinal)
                            ? IStatusProvider.StatusLevel.Error
                            : IStatusProvider.StatusLevel.Warning;
            SetRunStatus(run, level, message);

            if (run.Id != Volatile.Read(ref _activeRunId) || message == _lastLoggedFailure)
                return;

            _lastLoggedFailure = message;
            Log.Warning($"VideoStreamInput: {message}", this);
        }

        private void SetOwnStatus(string message)
        {
            var level = message.StartsWith("Error", StringComparison.Ordinal) ? IStatusProvider.StatusLevel.Error
                        : message.StartsWith("Warning", StringComparison.Ordinal) ? IStatusProvider.StatusLevel.Warning
                        : IStatusProvider.StatusLevel.Notice;
            lock (_frameLock)
            {
                _status = new StatusSnapshot(level, message);
            }
        }

        /** Sleeps in short slices so a cancel is observed promptly. Returns true when cancelled. */
        private static bool WaitOrCancelled(CancellationToken token, int milliseconds)
        {
            var deadline = Environment.TickCount64 + milliseconds;
            while (!token.IsCancellationRequested)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                    return false;

                Thread.Sleep((int)Math.Min(remaining, 50));
            }

            return true;
        }

        /// <summary>The URL an encoder should push to — the first usable LAN address, so the status message is
        /// directly copyable on a phone on the same network. Re-read per listen attempt so a laptop that changes
        /// networks advertises its current address.</summary>
        private static string BuildRtmpServerUrl(int port)
        {
            var interfaces = LanInterfaces.GetUsable();
            var address = interfaces.Count > 0 ? interfaces[0].Address.ToString() : "127.0.0.1";
            return $"rtmp://{address}:{port}/{RtmpListenPath}";
        }

        private static IReadOnlyDictionary<string, string>? BuildDemuxerOptions(string url)
        {
            // RTMP: keep the client-side buffer short instead of the 3 s default. rtmp_live is left at "any" so a
            // looped/recorded RTMP source still plays; the server path sets it explicitly.
            if (url.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, string>
                           {
                               ["rtmp_buffer"] = RtmpClientBufferMs,
                           };
            }

            // RTSP over TCP survives lossy networks and NAT far better than the UDP default; the socket timeout
            // (microseconds for RTSP) makes a dead camera fail the open instead of waiting on the watchdog.
            if (url.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, string>
                           {
                               ["rtsp_transport"] = "tcp",
                               ["timeout"] = "5000000",
                           };
            }

            return null;
        }

        private unsafe void UploadFrameToGpu(RgbaFrame frame)
        {
            if (frame.Width <= 0 || frame.Height <= 0)
                return;

            if (_gpuTexture == null || _gpuTexture.Description.Width != frame.Width || _gpuTexture.Description.Height != frame.Height)
            {
                GraphicsUtilities.Dispose(ref _gpuTexture);
                _gpuTexture = Texture2D.CreateTexture2D(new Texture2DDescription
                                                            {
                                                                Width = frame.Width,
                                                                Height = frame.Height,
                                                                MipLevels = 1,
                                                                ArraySize = 1,
                                                                Format = Format.R8G8B8A8_UNorm,
                                                                SampleDescription = new SampleDescription(1, 0),
                                                                Usage = ResourceUsage.Default,
                                                                BindFlags = BindFlags.ShaderResource,
                                                                CpuAccessFlags = CpuAccessFlags.None,
                                                                OptionFlags = ResourceOptionFlags.None
                                                            });
            }

            fixed (byte* pixels = frame.Pixels)
            {
                var dataBox = new DataBox((IntPtr)pixels, frame.Width * BytesPerPixel, 0);
                ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _gpuTexture, 0);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            // No IsDisposed guard: the instance is flagged disposed before this runs, so a guard here would skip
            // the teardown and leave the capture thread running.
            if (isDisposing)
            {
                // Never joins: the run disposes its own FFmpeg session, and its frames are ignored once the
                // active id is cleared, so the editor does not wait on a slow network close.
                StopCapture("Disconnected");
                GraphicsUtilities.Dispose(ref _gpuTexture);
            }

            base.Dispose(isDisposing);
        }

        /// <summary>One connection attempt (pull or RTMP server) and the thread running it.</summary>
        private sealed class CaptureRun(int id)
        {
            public readonly int Id = id;

            /// <summary>
            /// Deliberately never disposed: the eval thread may still call <c>Cancel</c> while the capture thread
            /// exits, and a source without a wait handle or registrations holds nothing that needs releasing.
            /// </summary>
            public readonly CancellationTokenSource Cancellation = new();

            public CancellationToken Token => Cancellation.Token;
            public Thread? Thread;
            public long RetiredAtMs;
            public volatile bool HasExited;
        }

        /// <summary>An immutable status pair. Swapped as one reference so a reader never sees a torn level/message.</summary>
        private sealed class StatusSnapshot(IStatusProvider.StatusLevel level, string message)
        {
            public IStatusProvider.StatusLevel Level { get; } = level;
            public string Message { get; } = message;
        }

        /// <summary>A packed RGBA8 frame in a reusable buffer; three of these rotate between capture and upload.</summary>
        private sealed class RgbaFrame
        {
            public byte[] Pixels = [];
            public int Width;
            public int Height;

            public void EnsureCapacity(int width, int height)
            {
                var byteCount = width * height * BytesPerPixel;
                if (Pixels.Length < byteCount)
                    Pixels = new byte[byteCount];

                Width = width;
                Height = height;
            }
        }

        private const int NoRunId = 0;
        private const int BytesPerPixel = 4;

        /** How long a run that ended on its own waits before reconnecting. */
        private const long AutoReconnectDelayMs = 3000;

        /** A single open or packet read blocking longer than this counts as a dead source and triggers a reconnect. */
        private static readonly TimeSpan StreamStallTimeout = TimeSpan.FromSeconds(10);

        /** How long a cancelled run may take to exit before the next run starts without it. */
        private const long AbandonStuckRunAfterMs = 10_000;

        /** One listen attempt's wait for a publisher. Short, so the advertised address is refreshed regularly. */
        private const int RtmpListenTimeoutSec = 1;

        /** A listen attempt failing faster than this did not wait for a publisher, i.e. it could not bind the port. */
        private const long ImmediateListenFailureMs = 500;

        private const int ListenRetryDelayMs = 2000;
        private const int PublisherReconnectGraceMs = 200;
        private const string RtmpClientBufferMs = "100";

        /** The path advertised to encoders. FFmpeg's server accepts any stream key (it only warns about a mismatch). */
        private const string RtmpListenPath = "live";

        /** Guards the frame slots, the active run id, and the status pair across the eval and capture threads. */
        private readonly Lock _frameLock = new();

        private RgbaFrame _pendingFrame = new();
        private RgbaFrame _uploadFrame = new();
        private bool _hasPendingFrame;
        private bool _frameAvailable;
        private int _activeRunId = NoRunId;

        private volatile StatusSnapshot _status = new(IStatusProvider.StatusLevel.Notice, "Not connected.");
        private volatile string? _lastLoggedFailure;

        // Eval-thread state.
        private CaptureRun? _activeRun;
        private readonly List<CaptureRun> _retiredRuns = [];
        private int _lastRunId;
        private long _lastRunStartMs;
        private Texture2D? _gpuTexture;
        private bool _restartRequested;
        private bool _lastConnectState;
        private bool _lastRtmpServerState;
        private int _lastRtmpPort;
        private string _lastUrl = string.Empty;
    }
}
