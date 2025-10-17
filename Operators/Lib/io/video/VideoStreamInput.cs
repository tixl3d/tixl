#nullable enable
using System.Threading;
using OpenCvSharp;
using SharpDX;
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

        [Output(Guid = "3F6A960C-906A-4073-A338-ABB785869062", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Int2> Resolution = new();

        [Output(Guid = "B0E4313B-A746-4444-934E-14285D42DADB", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public new readonly Slot<string> Status = new();

        [Output(Guid = "2E7E2404-5881-4327-9653-CA9533B856A9", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Texture2D?> Texture = new();

        [Input(Guid = "A8B0971B-94CB-4C3D-932D-337581B8D83A")]
        public readonly InputSlot<string> Url = new("rtsp://your_stream_url");

        private CancellationTokenSource? _cancellationTokenSource;
        private VideoCapture? _capture;

        private Thread? _captureThread;

        private Texture2D? _gpuTexture;
        private bool _lastConnectState;
        private volatile string _lastStatusMessage = "Not connected.";

        private string _lastUrl = string.Empty;
        private Mat? _sharedBgraMat;

        public VideoStreamInput()
        {
            // CORRECT: Only the primary data output should trigger the update cycle.
            Texture.UpdateAction = Update;

            // REMOVED: These lines were causing the infinite recursion.
            // Status.UpdateAction = Update;
            // Resolution.UpdateAction = Update;
        }

        public IStatusProvider.StatusLevel GetStatusLevel()
        {
            return _lastStatusMessage.StartsWith("Error") ? IStatusProvider.StatusLevel.Error : IStatusProvider.StatusLevel.Success;
        }

        public string GetStatusMessage()
        {
            return _lastStatusMessage;
        }
        //private bool _disposed;

        private void SetStatus(string message) => _lastStatusMessage = message;

        private void Update(EvaluationContext context)
        {
            var url = Url.GetValue(context);
            var shouldConnect = Connect.GetValue(context);
            var reconnect = Reconnect.GetValue(context);
            if (reconnect) Reconnect.SetTypedInputValue(false);

            bool settingsChanged = shouldConnect != _lastConnectState || (shouldConnect && url != _lastUrl);

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
                if (_sharedBgraMat != null && !_sharedBgraMat.Empty())
                {
                    UploadMatToGpu(_sharedBgraMat);
                    Texture.Value = _gpuTexture;
                    Resolution.Value = new Int2(_sharedBgraMat.Width, _sharedBgraMat.Height);
                }
                else
                {
                    // If there's no frame, ensure the output is cleared
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
            _capture?.Dispose();
            _capture = null;
            SetStatus("Disconnected");
        }

        private void CaptureLoop(string url, CancellationToken token)
        {
            try
            {
                SetStatus($"Connecting to {url}...");

                _capture = new VideoCapture(url, VideoCaptureAPIs.FFMPEG);

                if (token.IsCancellationRequested || !_capture.IsOpened())
                {
                    SetStatus($"Error: Failed to open stream '{url}'.");
                    _capture.Dispose();
                    return;
                }

                SetStatus("Streaming");

                using var frame = new Mat();

                while (!token.IsCancellationRequested)
                {
                    if (_capture.Read(frame) && !frame.Empty())
                    {
                        lock (_lockObject)
                        {
                            if (_sharedBgraMat == null || _sharedBgraMat.Width != frame.Width || _sharedBgraMat.Height != frame.Height)
                            {
                                _sharedBgraMat?.Dispose();
                                _sharedBgraMat = new Mat(frame.Rows, frame.Cols, MatType.CV_8UC4);
                            }

                            Cv2.CvtColor(frame, _sharedBgraMat, ColorConversionCodes.BGR2BGRA);
                        }
                    }
                    else if (_capture.IsOpened())
                    {
                        SetStatus("Warning: Stream interrupted. Waiting...");
                        Thread.Sleep(500);
                    }
                    else
                    {
                        break;
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
                _capture?.Dispose();
                lock (_lockObject)
                {
                    _sharedBgraMat?.Dispose();
                    _sharedBgraMat = null;
                }
            }
        }

        private void UploadMatToGpu(Mat mat)
        {
            if (mat.Empty()) return;

            var width = mat.Width;
            var height = mat.Height;

            if (_gpuTexture == null || _gpuTexture.Description.Width != width || _gpuTexture.Description.Height != height)
            {
                Utilities.Dispose(ref _gpuTexture);

                var texDesc = new Texture2DDescription
                                  {
                                      Width = width,
                                      Height = height,
                                      MipLevels = 1,
                                      ArraySize = 1,
                                      Format = Format.B8G8R8A8_UNorm,
                                      SampleDescription = new SampleDescription(1, 0),
                                      Usage = ResourceUsage.Default,
                                      BindFlags = BindFlags.ShaderResource,
                                      CpuAccessFlags = CpuAccessFlags.None,
                                      OptionFlags = ResourceOptionFlags.None
                                  };
                _gpuTexture = Texture2D.CreateTexture2D(texDesc);
            }

            var dataBox = new DataBox(mat.Data, (int)mat.Step(), 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _gpuTexture);
        }

        // public void Dispose()
        // {
        //     Dispose(true);
        //     GC.SuppressFinalize(this);
        // }

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
                _sharedBgraMat?.Dispose();
            }
        }
    }
}