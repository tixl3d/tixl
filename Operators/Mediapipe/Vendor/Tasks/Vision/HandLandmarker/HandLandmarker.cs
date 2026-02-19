using Mediapipe.Framework;
using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Packet;
using Mediapipe.Gpu;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;

namespace Mediapipe.Tasks.Vision.HandLandmarker;

public sealed class HandLandmarker : BaseVisionTaskApi
{
    private const string _IMAGE_IN_STREAM_NAME = "image_in";
    private const string _IMAGE_OUT_STREAM_NAME = "image_out";
    private const string _IMAGE_TAG = "IMAGE";
    private const string _NORM_RECT_STREAM_NAME = "norm_rect_in";
    private const string _NORM_RECT_TAG = "NORM_RECT";
    private const string _HANDEDNESS_STREAM_NAME = "handedness";
    private const string _HANDEDNESS_TAG = "HANDEDNESS";
    private const string _HAND_LANDMARKS_STREAM_NAME = "landmarks";
    private const string _HAND_LANDMARKS_TAG = "LANDMARKS";
    private const string _HAND_WORLD_LANDMARKS_STREAM_NAME = "world_landmarks";
    private const string _HAND_WORLD_LANDMARKS_TAG = "WORLD_LANDMARKS";
    private const string _TASK_GRAPH_NAME = "mediapipe.tasks.vision.hand_landmarker.HandLandmarkerGraph";

    private const int _MICRO_SECONDS_PER_MILLISECOND = 1000;

    private readonly NormalizedRect _normalizedRect = new();

    private HandLandmarker(
        CalculatorGraphConfig graphConfig,
        VisionRunningMode runningMode,
        GpuResources? gpuResources,
        TaskRunner.PacketsCallback? packetCallback) : base(graphConfig, runningMode, gpuResources, packetCallback)
    {
    }

    /// <summary>
    ///     Creates an <see cref="HandLandmarker" /> object from a TensorFlow Lite model and the default
    ///     <see cref="HandLandmarkerOptions" />.
    ///     Note that the created <see cref="HandLandmarker" /> instance is in image mode,
    ///     for detecting hand landmarks on single image inputs.
    /// </summary>
    /// <param name="modelPath">Path to the model.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="HandLandmarker" /> object that's created from the model and the default
    ///     <see cref="HandLandmarkerOptions" />.
    /// </returns>
    public static HandLandmarker CreateFromModelPath(string modelPath, GpuResources? gpuResources = null)
    {
        CoreBaseOptions baseOptions = new(modelAssetPath: modelPath);
        HandLandmarkerOptions options = new(baseOptions);
        return CreateFromOptions(options, gpuResources);
    }

    /// <summary>
    ///     Creates the <see cref="HandLandmarker" /> object from <paramref name="HandLandmarkerOptions" />.
    /// </summary>
    /// <param name="options">Options for the hand landmarker task.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="HandLandmarker" /> object that's created from <paramref name="options" />.
    /// </returns>
    public static HandLandmarker CreateFromOptions(HandLandmarkerOptions options, GpuResources? gpuResources = null)
    {
        TaskInfo<HandLandmarkerOptions> taskInfo = new(
            _TASK_GRAPH_NAME,
            [
                string.Join(":", _IMAGE_TAG, _IMAGE_IN_STREAM_NAME),
                string.Join(":", _NORM_RECT_TAG, _NORM_RECT_STREAM_NAME)
            ],
            [
                string.Join(":", _HANDEDNESS_TAG, _HANDEDNESS_STREAM_NAME),
                string.Join(":", _HAND_LANDMARKS_TAG, _HAND_LANDMARKS_STREAM_NAME),
                string.Join(":", _HAND_WORLD_LANDMARKS_TAG, _HAND_WORLD_LANDMARKS_STREAM_NAME),
                string.Join(":", _IMAGE_TAG, _IMAGE_OUT_STREAM_NAME)
            ],
            options);

        return new HandLandmarker(
            taskInfo.GenerateGraphConfig(options.RunningMode == VisionRunningMode.LIVE_STREAM),
            options.RunningMode,
            gpuResources,
            BuildPacketsCallback(options));
    }

    /// <summary>
    ///     Performs hand landmarks detection on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="HandLandmarker" /> is created with the image running mode.
    ///     The image can be of any size with format RGB or RGBA.
    /// </summary>
    /// <param name="image">MediaPipe Image.</param>
    /// <param name="imageProcessingOptions">Options for image processing.</param>
    /// <returns>
    ///     The hand landmarks detection results.
    /// </returns>
    public HandLandmarkerResult Detect(Image image, ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = DetectInternal(image, imageProcessingOptions);

        HandLandmarkerResult result = default;
        _ = TryBuildHandLandmarkerResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs hand landmarks detection on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="HandLandmarker" /> is created with the image running mode.
    ///     The image can be of any size with format RGB or RGBA.
    /// </summary>
    /// <param name="image">MediaPipe Image.</param>
    /// <param name="imageProcessingOptions">Options for image processing.</param>
    /// <param name="result">
    ///     <see cref="HandLandmarkerResult" /> to which the result will be written.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if some faces are detected, <see langword="false" /> otherwise.
    /// </returns>
    public bool TryDetect(Image image, ImageProcessingOptions? imageProcessingOptions, ref HandLandmarkerResult result)
    {
        using PacketMap outputPackets = DetectInternal(image, imageProcessingOptions);
        return TryBuildHandLandmarkerResult(outputPackets, ref result);
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
    ///     Performs hand landmarks detection on the provided video frames.
    ///     Only use this method when the HandLandmarker is created with the video
    ///     running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <returns>
    ///     The hand landmarks detection results.
    /// </returns>
    public HandLandmarkerResult DetectForVideo(Image image, long timestampMillisec,
        ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = DetectForVideoInternal(image, timestampMillisec, imageProcessingOptions);

        HandLandmarkerResult result = default;
        _ = TryBuildHandLandmarkerResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs hand landmarks detection on the provided video frames.
    ///     Only use this method when the HandLandmarker is created with the video
    ///     running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <remarks>
    ///     When hands are not found, <paramref name="result" /> won't be overwritten.
    /// </remarks>
    /// <param name="result">
    ///     <see cref="HandLandmarkerResult" /> to which the result will be written.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if some hands are detected, <see langword="false" /> otherwise.
    /// </returns>
    public bool TryDetectForVideo(Image image, long timestampMillisec, ImageProcessingOptions? imageProcessingOptions,
        ref HandLandmarkerResult result)
    {
        using PacketMap outputPackets = DetectForVideoInternal(image, timestampMillisec, imageProcessingOptions);
        return TryBuildHandLandmarkerResult(outputPackets, ref result);
    }

    private PacketMap DetectForVideoInternal(Image image, long timestampMillisec,
        ImageProcessingOptions? imageProcessingOptions = null)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);
        long timestampMicrosec = timestampMillisec * _MICRO_SECONDS_PER_MILLISECOND;

        PacketMap packetMap = new();
        packetMap.Emplace(_IMAGE_IN_STREAM_NAME, PacketHelper.CreateImageAt(image, timestampMicrosec));
        packetMap.Emplace(_NORM_RECT_STREAM_NAME, PacketHelper.CreateProtoAt(_normalizedRect, timestampMicrosec));

        return ProcessVideoData(packetMap);
    }

    /// <summary>
    ///     Sends live image data to perform hand landmarks detection.
    ///     Only use this method when the HandLandmarker is created with the live stream
    ///     running mode. The input timestamps should be monotonically increasing for
    ///     adjacent calls of this method. This method will return immediately after the
    ///     input image is accepted. The results will be available via the
    ///     <see cref="HandLandmarkerOptions.ResultCallbackFunc" /> provided in the <see cref="HandLandmarkerOptions" />.
    ///     The <see cref="DetectAsync" /> method is designed to process live stream data such as camera
    ///     input. To lower the overall latency, hand landmarker may drop the input
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

    private static TaskRunner.PacketsCallback? BuildPacketsCallback(HandLandmarkerOptions options)
    {
        HandLandmarkerOptions.ResultCallbackFunc? resultCallback = options.ResultCallback;
        if (resultCallback == null) return null;

        HandLandmarkerResult handLandmarkerResult = HandLandmarkerResult.Alloc(options.NumHands);

        return outputPackets =>
        {
            using Packet<Image>? outImagePacket = outputPackets.At<Image>(_IMAGE_OUT_STREAM_NAME);
            if (outImagePacket == null || outImagePacket.IsEmpty()) return;

            using Image image = outImagePacket.Get();
            long timestamp = outImagePacket.TimestampMicroseconds() / _MICRO_SECONDS_PER_MILLISECOND;

            if (TryBuildHandLandmarkerResult(outputPackets, ref handLandmarkerResult))
                resultCallback(handLandmarkerResult, image, timestamp);
            else
                resultCallback(default, image, timestamp);
        };
    }

    private static bool TryBuildHandLandmarkerResult(PacketMap outputPackets, ref HandLandmarkerResult result)
    {
        using Packet<List<NormalizedLandmarks>> handLandmarksPacket =
            outputPackets.At<List<NormalizedLandmarks>>(_HAND_LANDMARKS_STREAM_NAME);
        if (handLandmarksPacket.IsEmpty()) return false;

        List<NormalizedLandmarks> handLandmarks = result.HandLandmarks ?? [];
        handLandmarksPacket.Get(handLandmarks);

        using Packet<List<Classifications>> handednessPacket =
            outputPackets.At<List<Classifications>>(_HANDEDNESS_STREAM_NAME);
        List<Classifications> handedness = result.Handedness ?? [];
        handednessPacket.Get(handedness);

        using Packet<List<Landmarks>> handWorldLandmarksPacket =
            outputPackets.At<List<Landmarks>>(_HAND_WORLD_LANDMARKS_STREAM_NAME);
        List<Landmarks> handWorldLandmarks = result.HandWorldLandmarks ?? [];
        handWorldLandmarksPacket.Get(handWorldLandmarks);

        result = new HandLandmarkerResult(handedness, handLandmarks, handWorldLandmarks);
        return true;
    }
}