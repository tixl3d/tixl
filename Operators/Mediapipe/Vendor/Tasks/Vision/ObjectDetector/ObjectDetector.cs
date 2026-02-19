using Mediapipe.Framework;
using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Packet;
using Mediapipe.Gpu;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using ObjectDetectorResult = Mediapipe.Tasks.Components.Containers.DetectionResult;

namespace Mediapipe.Tasks.Vision.ObjectDetector;

public sealed class ObjectDetector : BaseVisionTaskApi
{
    private const string _DETECTIONS_OUT_STREAM_NAME = "detections_out";
    private const string _DETECTIONS_TAG = "DETECTIONS";
    private const string _NORM_RECT_STREAM_NAME = "norm_rect_in";
    private const string _NORM_RECT_TAG = "NORM_RECT";
    private const string _IMAGE_IN_STREAM_NAME = "image_in";
    private const string _IMAGE_OUT_STREAM_NAME = "image_out";
    private const string _IMAGE_TAG = "IMAGE";
    private const string _TASK_GRAPH_NAME = "mediapipe.tasks.vision.ObjectDetectorGraph";

    private const int _MICRO_SECONDS_PER_MILLISECOND = 1000;

    private readonly NormalizedRect _normalizedRect = new();

    private ObjectDetector(
        CalculatorGraphConfig graphConfig,
        VisionRunningMode runningMode,
        GpuResources? gpuResources,
        TaskRunner.PacketsCallback? packetCallback) : base(graphConfig, runningMode, gpuResources, packetCallback)
    {
    }

    /// <summary>
    ///     Creates an <see cref="ObjectDetector" /> object from a TensorFlow Lite model and the default
    ///     <see cref="ObjectDetectorOptions" />.
    ///     Note that the created <see cref="ObjectDetector" /> instance is in image mode,
    ///     for detecting objects on single image inputs.
    /// </summary>
    /// <param name="modelPath">Path to the model.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="ObjectDetector" /> object that's created from the model and the default
    ///     <see cref="ObjectDetectorOptions" />.
    /// </returns>
    public static ObjectDetector CreateFromModelPath(string modelPath, GpuResources? gpuResources = null)
    {
        CoreBaseOptions baseOptions = new(modelAssetPath: modelPath);
        ObjectDetectorOptions options = new(baseOptions);
        return CreateFromOptions(options, gpuResources);
    }

    /// <summary>
    ///     Creates the <see cref="ObjectDetector" /> object from <paramref name="ObjectDetectorOptions" />.
    /// </summary>
    /// <param name="options">Options for the object detector task.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="ObjectDetector" /> object that's created from <paramref name="options" />.
    /// </returns>
    public static ObjectDetector CreateFromOptions(ObjectDetectorOptions options, GpuResources? gpuResources = null)
    {
        TaskInfo<ObjectDetectorOptions> taskInfo = new(
            _TASK_GRAPH_NAME,
            [
                string.Join(":", _IMAGE_TAG, _IMAGE_IN_STREAM_NAME),
                string.Join(":", _NORM_RECT_TAG, _NORM_RECT_STREAM_NAME)
            ],
            [
                string.Join(":", _DETECTIONS_TAG, _DETECTIONS_OUT_STREAM_NAME),
                string.Join(":", _IMAGE_TAG, _IMAGE_OUT_STREAM_NAME)
            ],
            options);

        return new ObjectDetector(
            taskInfo.GenerateGraphConfig(options.RunningMode == VisionRunningMode.LIVE_STREAM),
            options.RunningMode,
            gpuResources,
            BuildPacketsCallback(options));
    }

    /// <summary>
    ///     Performs object detection on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="ObjectDetector" /> is created with the image running mode.
    /// </summary>
    /// <returns>
    ///     A object detection result object that contains a list of object detections,
    ///     each detection has a bounding box that is expressed in the unrotated input
    ///     frame of reference coordinates system, i.e. in `[0,image_width) x [0,
    ///     image_height)`, which are the dimensions of the underlying image data.
    /// </returns>
    public ObjectDetectorResult Detect(Image image, ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = DetectInternal(image, imageProcessingOptions);

        ObjectDetectorResult result = default;
        _ = TryBuildObjectDetectorResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs object detection on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="ObjectDetector" /> is created with the image running mode.
    /// </summary>
    /// <remarks>
    ///     When objects are not found, <paramref name="result" /> won't be overwritten.
    /// </remarks>
    /// <param name="result">
    ///     <see cref="ObjectDetectorResult" /> to which the result will be written.
    ///     A object detection result object that contains a list of object detections,
    ///     each detection has a bounding box that is expressed in the unrotated input
    ///     frame of reference coordinates system, i.e. in `[0,image_width) x [0,
    ///     image_height)`, which are the dimensions of the underlying image data.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if some objects are detected, <see langword="false" /> otherwise.
    /// </returns>
    public bool TryDetect(Image image, ImageProcessingOptions? imageProcessingOptions, ref ObjectDetectorResult result)
    {
        using PacketMap outputPackets = DetectInternal(image, imageProcessingOptions);
        return TryBuildObjectDetectorResult(outputPackets, ref result);
    }

    private PacketMap DetectInternal(Image image, ImageProcessingOptions? imageProcessingOptions)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);

        PacketMap packetMap = new();
        packetMap.Emplace(_IMAGE_IN_STREAM_NAME, PacketHelper.CreateImage(image));
        packetMap.Emplace(_NORM_RECT_STREAM_NAME, PacketHelper.CreateProto(_normalizedRect));

        return ProcessImageData(packetMap);
    }

    /// <summary>
    ///     Performs object detection on the provided video frames.
    ///     Only use this method when the <see cref="ObjectDetector" /> is created with the video
    ///     running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <returns>
    ///     A object detection result object that contains a list of object detections,
    ///     each detection has a bounding box that is expressed in the unrotated input
    ///     frame of reference coordinates system, i.e. in `[0,image_width) x [0,
    ///     image_height)`, which are the dimensions of the underlying image data.
    /// </returns>
    public ObjectDetectorResult DetectForVideo(Image image, long timestampMillisec,
        ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = DetectForVideoInternal(image, timestampMillisec, imageProcessingOptions);

        ObjectDetectorResult result = default;
        _ = TryBuildObjectDetectorResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs object detection on the provided video frames.
    ///     Only use this method when the <see cref="ObjectDetector" /> is created with the video
    ///     running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <remarks>
    ///     When objects are not found, <paramref name="result" /> won't be overwritten.
    /// </remarks>
    /// <param name="result">
    ///     <see cref="ObjectDetectorResult" /> to which the result will be written.
    ///     A object detection result object that contains a list of object detections,
    ///     each detection has a bounding box that is expressed in the unrotated input
    ///     frame of reference coordinates system, i.e. in `[0,image_width) x [0,
    ///     image_height)`, which are the dimensions of the underlying image data.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if some objects are detected, <see langword="false" /> otherwise.
    /// </returns>
    public bool TryDetectForVideo(Image image, long timestampMillisec, ImageProcessingOptions? imageProcessingOptions,
        ref ObjectDetectorResult result)
    {
        using PacketMap outputPackets = DetectForVideoInternal(image, timestampMillisec, imageProcessingOptions);
        return TryBuildObjectDetectorResult(outputPackets, ref result);
    }

    private PacketMap DetectForVideoInternal(Image image, long timestampMillisec,
        ImageProcessingOptions? imageProcessingOptions)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);
        long timestampMicrosec = timestampMillisec * _MICRO_SECONDS_PER_MILLISECOND;

        PacketMap packetMap = new();
        packetMap.Emplace(_IMAGE_IN_STREAM_NAME, PacketHelper.CreateImageAt(image, timestampMicrosec));
        packetMap.Emplace(_NORM_RECT_STREAM_NAME, PacketHelper.CreateProtoAt(_normalizedRect, timestampMicrosec));

        return ProcessVideoData(packetMap);
    }

    /// <summary>
    ///     Sends live image data (an Image with a unique timestamp) to perform object detection.
    ///     Only use this method when the <see cref="ObjectDetector" /> is created with the live stream
    ///     running mode. The input timestamps should be monotonically increasing for
    ///     adjacent calls of this method. This method will return immediately after the
    ///     input image is accepted. The results will be available via the
    ///     <see cref="ObjectDetectorOptions.ResultCallbackFunc" /> provided in the <see cref="ObjectDetectorOptions" />.
    ///     The <see cref="DetectAsync" /> method is designed to process live stream data such as camera
    ///     input. To lower the overall latency, object detector may drop the input
    ///     images if needed. In other words, it's not guaranteed to have output per
    ///     input image.
    /// </summary>
    public void DetectAsync(Image image, long timestampMillisec, ImageProcessingOptions? imageProcessingOptions = null)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);
        long timestampMicrosec = timestampMillisec * _MICRO_SECONDS_PER_MILLISECOND;

        PacketMap packetMap = new();
        packetMap.Emplace(_IMAGE_IN_STREAM_NAME, PacketHelper.CreateImageAt(image, timestampMicrosec));
        packetMap.Emplace(_NORM_RECT_STREAM_NAME, PacketHelper.CreateProtoAt(_normalizedRect, timestampMicrosec));

        SendLiveStreamData(packetMap);
    }

    private static TaskRunner.PacketsCallback? BuildPacketsCallback(ObjectDetectorOptions options)
    {
        ObjectDetectorOptions.ResultCallbackFunc? resultCallback = options.ResultCallback;
        if (resultCallback == null) return null;

        DetectionResult result = ObjectDetectorResult.Alloc(Math.Max(options.MaxResults ?? 0, 0));

        return outputPackets =>
        {
            using Packet<Image>? outImagePacket = outputPackets.At<Image>(_IMAGE_OUT_STREAM_NAME);
            if (outImagePacket == null || outImagePacket.IsEmpty()) return;

            using Image image = outImagePacket.Get();
            long timestamp = outImagePacket.TimestampMicroseconds() / _MICRO_SECONDS_PER_MILLISECOND;

            if (TryBuildObjectDetectorResult(outputPackets, ref result))
                resultCallback(result, image, timestamp);
            else
                resultCallback(default, image, timestamp);
        };
    }

    private static bool TryBuildObjectDetectorResult(PacketMap outputPackets, ref ObjectDetectorResult result)
    {
        using Packet<ObjectDetectorResult> detectionsPacket =
            outputPackets.At<ObjectDetectorResult>(_DETECTIONS_OUT_STREAM_NAME);
        if (detectionsPacket.IsEmpty()) return false;
        detectionsPacket.Get(ref result);
        return true;
    }
}