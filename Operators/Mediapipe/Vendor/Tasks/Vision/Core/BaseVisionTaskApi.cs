using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Packet;
using Mediapipe.Gpu;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;

namespace Mediapipe.Tasks.Vision.Core;

/// <summary>
///     The base class of the user-facing mediapipe vision task api classes.
/// </summary>
public class BaseVisionTaskApi : IDisposable
{
    private readonly TaskRunner _taskRunner;
    private bool _isClosed;

    /// <summary>
    ///     Initializes the `BaseVisionTaskApi` object.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     The packet callback is not properly set based on the task's running mode.
    /// </exception>
    protected BaseVisionTaskApi(
        CalculatorGraphConfig graphConfig,
        VisionRunningMode runningMode,
        TaskRunner.PacketsCallback packetsCallback) : this(graphConfig, runningMode, null, packetsCallback)
    {
    }

    /// <summary>
    ///     Initializes the `BaseVisionTaskApi` object.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     The packet callback is not properly set based on the task's running mode.
    /// </exception>
    protected BaseVisionTaskApi(
        CalculatorGraphConfig graphConfig,
        VisionRunningMode runningMode,
        GpuResources? gpuResources,
        TaskRunner.PacketsCallback? packetsCallback)
    {
        if (runningMode == VisionRunningMode.LIVE_STREAM)
        {
            if (packetsCallback == null)
                throw new ArgumentException(
                    "The vision task is in live stream mode, a user-defined result callback must be provided.");
        }
        else if (packetsCallback != null)
        {
            throw new ArgumentException(
                "The vision task is in image or video mode, a user-defined result callback should not be provided.");
        }

        (int callbackId, TaskRunner.NativePacketsCallback? nativePacketsCallback) =
            PacketsCallbackTable.Add(packetsCallback);

        if (gpuResources != null)
            _taskRunner = TaskRunner.Create(graphConfig, gpuResources, callbackId, nativePacketsCallback);
        else
            _taskRunner = TaskRunner.Create(graphConfig, callbackId, nativePacketsCallback);
        RunningMode = runningMode;
    }

    public VisionRunningMode RunningMode { get; }

    void IDisposable.Dispose()
    {
        if (!_isClosed) Close();
        _taskRunner.Dispose();
    }

    /// <summary>
    ///     A synchronous method to process single image inputs.
    ///     The call blocks the current thread until a failure status or a successful result is returned.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     If the task's running mode is not set to the image mode.
    /// </exception>
    protected PacketMap ProcessImageData(PacketMap inputs)
    {
        if (RunningMode != VisionRunningMode.IMAGE)
            throw new InvalidOperationException(
                $"Task is not initialized with the image mode. Current running mode: {RunningMode}");
        return _taskRunner.Process(inputs);
    }

    /// <summary>
    ///     A synchronous method to process continuous video frames.
    ///     The call blocks the current thread until a failure status or a successful result is returned.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     If the task's running mode is not set to the video mode.
    /// </exception>
    protected PacketMap ProcessVideoData(PacketMap inputs)
    {
        if (RunningMode != VisionRunningMode.VIDEO)
            throw new InvalidOperationException(
                $"Task is not initialized with the video mode. Current running mode: {RunningMode}");
        return _taskRunner.Process(inputs);
    }

    /// <summary>
    ///     An asynchronous method to send live stream data to the runner.
    ///     The results will be available in the user-defined results callback.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     If the task's running mode is not set to the live stream mode.
    /// </exception>
    protected void SendLiveStreamData(PacketMap inputs)
    {
        if (RunningMode != VisionRunningMode.LIVE_STREAM)
            throw new InvalidOperationException(
                $"Task is not initialized with the live stream mode. Current running mode: {RunningMode}");
        _taskRunner.Send(inputs);
    }

    private static void ResetNormalizedRect(NormalizedRect normalizedRect)
    {
        normalizedRect.Rotation = 0;
        normalizedRect.XCenter = 0.5f;
        normalizedRect.YCenter = 0.5f;
        normalizedRect.Width = 1;
        normalizedRect.Height = 1;
    }

    protected static void ConfigureNormalizedRect(NormalizedRect target, ImageProcessingOptions? options, Image image,
        bool roiAllowed = true)
    {
        ResetNormalizedRect(target);

        if (options is not ImageProcessingOptions optionsValue) return;

        if (optionsValue.RotationDegrees % 90 != 0)
            throw new ArgumentException("Expected rotation to be a multiple of 90°.");

        // Convert to radians counter-clockwise.
        // TODO: use System.MathF.PI
        target.Rotation = -optionsValue.RotationDegrees * MathF.PI / 180.0f;

        if (optionsValue.RegionOfInterest is RectF roi)
        {
            if (!roiAllowed) throw new ArgumentException("This task doesn't support region-of-interest.");

            if (roi.left >= roi.right || roi.top >= roi.bottom)
                throw new ArgumentException("Expected RectF with left < right and top < bottom.");
            if (roi.left < 0 || roi.top < 0 || roi.right > 1 || roi.bottom > 1)
                throw new ArgumentException("Expected RectF values to be in [0,1].");

            target.XCenter = (roi.left + roi.right) / 2.0f;
            target.YCenter = (roi.top + roi.bottom) / 2.0f;
            target.Width = roi.right - roi.left;
            target.Height = roi.bottom - roi.top;
        }

        // For 90° and 270° rotations, we need to swap width and height.
        // This is due to the internal behavior of ImageToTensorCalculator, which:
        // - first denormalizes the provided rect by multiplying the rect width or
        //   height by the image width or height, respectively.
        // - then rotates this by denormalized rect by the provided rotation, and
        //   uses this for cropping,
        // - then finally rotates this back.
        // TODO: use System.MathF.Abs
        if (MathF.Abs(optionsValue.RotationDegrees % 180) != 0)
        {
            int ih = image.Height();
            int iw = image.Width();
            float w = target.Height * ih / iw;
            float h = target.Width * iw / ih;
            target.Width = w;
            target.Height = h;
        }
    }

    /// <summary>
    ///     Shuts down the mediapipe vision task instance.
    /// </summary>
    /// <exception cref="Exception">
    ///     If the mediapipe vision task failed to close.
    /// </exception>
    public void Close()
    {
        _taskRunner.Close();
        _isClosed = true;
    }

    /// <summary>
    ///     Returns the canonicalized CalculatorGraphConfig of the underlying graph.
    /// </summary>
    public CalculatorGraphConfig GetGraphConfig()
    {
        return _taskRunner.GetGraphConfig();
    }
}