#nullable enable
using System.Threading;
using SharpDX;
using T3.Core.Video;
using T3.VideoServices;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.io.video
{
    [Guid("D9A7233D-5D03-4268-A58B-465972852A5B")]
    internal sealed class VideoStreamInput : Instance<VideoStreamInput>, IStatusProvider
    {
        private readonly Lock _lockObject = new();

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

        private CancellationTokenSource? _cancellationTokenSource;
        private Thread? _captureThread;

        private Texture2D? _gpuTexture;
        private bool _lastConnectState;
        private volatile string _lastStatusMessage = "Not connected.";
        private string _lastUrl = string.Empty;

        // The most recent decoded frame as packed RGBA, shared between the capture thread and the render thread.
        private byte[]? _sharedRgba;
        private int _sharedWidth;
        private int _sharedHeight;
        private bool _hasFrame;

        public VideoStreamInput()
        {
            Texture.UpdateAction = Update;
        }

        public IStatusProvider.StatusLevel GetStatusLevel()
        {
            return _lastStatusMessage.StartsWith("Error") ? IStatusProvider.StatusLevel.Error : IStatusProvider.StatusLevel.Success;
        }

        public string GetStatusMessage()
        {
            return _lastStatusMessage;
        }

        private void SetStatus(string message) => _lastStatusMessage = message;

        private void Update(EvaluationContext context)
        {
            var url = Url.GetValue(context) ?? string.Empty;
            var shouldConnect = Connect.GetValue(context);
            var reconnect = Reconnect.GetValue(context);
            if (reconnect) Reconnect.SetTypedInputValue(false);

            var settingsChanged = shouldConnect != _lastConnectState || (shouldConnect && url != _lastUrl);

            if (shouldConnect)
            {
                if (IsCaptureThreadRunning() && (settingsChanged || reconnect)) StopCaptureThread();
                if (!IsCaptureThreadRunning())
                {
                    _lastUrl = url;
                    StartCaptureThread(url);
                }
            }
            else
            {
                StopCaptureThread();
            }

            _lastConnectState = shouldConnect;

            lock (_lockObject)
            {
                if (_hasFrame && _sharedRgba != null)
                {
                    UploadFrameToGpu(_sharedRgba, _sharedWidth, _sharedHeight);
                    Texture.Value = _gpuTexture;
                    Resolution.Value = new Int2(_sharedWidth, _sharedHeight);
                }
                else
                {
                    Texture.Value = null;
                }
            }

            // Update the status as a side effect. This does NOT trigger a new update.
            Status.Value = _lastStatusMessage;
        }

        private bool IsCaptureThreadRunning() => _captureThread is { IsAlive: true };

        private void StartCaptureThread(string url)
        {
            if (IsCaptureThreadRunning() || string.IsNullOrWhiteSpace(url)) return;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            _captureThread = new Thread(() => CaptureLoop(url, token))
                                 {
                                     IsBackground = true,
                                     Name = "VideoStream Capture Thread"
                                 };
            _captureThread.Start();
        }

        private void StopCaptureThread()
        {
            if (!IsCaptureThreadRunning()) return;

            _cancellationTokenSource?.Cancel();
            _captureThread?.Join(TimeSpan.FromSeconds(3));
            _captureThread = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            SetStatus("Disconnected");
        }

        private void CaptureLoop(string url, CancellationToken token)
        {
            VideoDecoderSession? session = null;
            SoftwareFrameConverter? converter = null;
            try
            {
                SetStatus($"Connecting to {url}...");

                // A live stream is consumed sequentially through this op's own SoftwareFrameConverter, so it needs
                // CPU-side frames — FastSeeking decodes in software, which provides exactly that.
                session = VideoDecoderSession.TryOpen(url, VideoPlaybackOptimization.FastSeeking, out var error, BuildDemuxerOptions(url));

                if (token.IsCancellationRequested)
                    return;

                if (session == null)
                {
                    SetStatus($"Error: Failed to open stream '{url}'. {error}");
                    return;
                }

                converter = new SoftwareFrameConverter(session.IsHdr);
                SetStatus("Streaming");

                while (!token.IsCancellationRequested)
                {
                    if (!session.TryReadNextFrame(out _))
                    {
                        SetStatus("Warning: Stream ended or interrupted.");
                        break;
                    }

                    var rgba = converter.Convert(session.CurrentFrame);
                    var byteCount = rgba.Width * rgba.Height * converter.BytesPerPixel;

                    lock (_lockObject)
                    {
                        if (_sharedRgba == null || _sharedRgba.Length < byteCount)
                            _sharedRgba = new byte[byteCount];

                        rgba.FillImageBuffer(_sharedRgba, 1);
                        _sharedWidth = rgba.Width;
                        _sharedHeight = rgba.Height;
                        _hasFrame = true;
                    }
                }
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                {
                    Log.Error($"Video stream thread failed: {e.Message}", this);
                    SetStatus($"Error: {e.Message}");
                }
            }
            finally
            {
                converter?.Dispose();
                session?.Dispose();
                lock (_lockObject)
                {
                    _hasFrame = false;
                }
            }
        }

        // TCP is more reliable than the default UDP, and a socket timeout keeps a dead URL from hanging the open.
        private static IReadOnlyDictionary<string, string>? BuildDemuxerOptions(string url)
        {
            if (!url.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase))
                return null;

            return new Dictionary<string, string>
                       {
                           ["rtsp_transport"] = "tcp",
                           ["timeout"] = "5000000", // microseconds
                       };
        }

        private unsafe void UploadFrameToGpu(byte[] buffer, int width, int height)
        {
            if (width <= 0 || height <= 0) return;

            if (_gpuTexture == null || _gpuTexture.Description.Width != width || _gpuTexture.Description.Height != height)
            {
                Utilities.Dispose(ref _gpuTexture);

                _gpuTexture = Texture2D.CreateTexture2D(new Texture2DDescription
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
                                                                OptionFlags = ResourceOptionFlags.None
                                                            });
            }

            fixed (byte* pixels = buffer)
            {
                var dataBox = new DataBox((IntPtr)pixels, width * 4, 0);
                ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _gpuTexture, 0);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            if (IsDisposed)
                return;

            if (!isDisposing)
                return;

            StopCaptureThread();
            Utilities.Dispose(ref _gpuTexture);
            lock (_lockObject)
            {
                _sharedRgba = null;
                _hasFrame = false;
            }
        }
    }
}
