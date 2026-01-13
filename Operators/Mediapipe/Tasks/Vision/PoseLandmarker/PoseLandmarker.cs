using Mediapipe.Framework;
using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Packet;
using Mediapipe.Gpu;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;

namespace Mediapipe.Tasks.Vision.PoseLandmarker;

public sealed class PoseLandmarker : BaseVisionTaskApi
{
    private const string _IMAGE_IN_STREAM_NAME = "image_in";
    private const string _IMAGE_OUT_STREAM_NAME = "image_out";
    private const string _IMAGE_TAG = "IMAGE";
    private const string _NORM_RECT_STREAM_NAME = "norm_rect_in";
    private const string _NORM_RECT_TAG = "NORM_RECT";
    private const string _SEGMENTATION_MASK_STREAM_NAME = "segmentation_mask";
    private const string _SEGMENTATION_MASK_TAG = "SEGMENTATION_MASK";
    private const string _NORM_LANDMARKS_STREAM_NAME = "norm_landmarks";
    private const string _NORM_LANDMARKS_TAG = "NORM_LANDMARKS";
    private const string _POSE_WORLD_LANDMARKS_STREAM_NAME = "world_landmarks";
    private const string _POSE_WORLD_LANDMARKS_TAG = "WORLD_LANDMARKS";
    private const string _TASK_GRAPH_NAME = "mediapipe.tasks.vision.pose_landmarker.PoseLandmarkerGraph";

    private const int _MICRO_SECONDS_PER_MILLISECOND = 1000;

    private readonly NormalizedRect _normalizedRect = new();

    private PoseLandmarker(
        CalculatorGraphConfig graphConfig,
        VisionRunningMode runningMode,
        GpuResources? gpuResources,
        TaskRunner.PacketsCallback? packetCallback) : base(graphConfig, runningMode, gpuResources, packetCallback)
    {
    }

    /// <summary>
    ///     Creates an <see cref="PoseLandmarker" /> object from a TensorFlow Lite model and the default
    ///     <see cref="PoseLandmarkerOptions" />.
    ///     Note that the created <see cref="PoseLandmarker" /> instance is in image mode,
    ///     for detecting pose landmarks on single image inputs.
    /// </summary>
    /// <param name="modelPath">Path to the model.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="PoseLandmarker" /> object that's created from the model and the default
    ///     <see cref="PoseLandmarkerOptions" />.
    /// </returns>
    public static PoseLandmarker CreateFromModelPath(string modelPath, GpuResources? gpuResources = null)
    {
        CoreBaseOptions baseOptions = new(modelAssetPath: modelPath);
        PoseLandmarkerOptions options = new(baseOptions);
        return CreateFromOptions(options, gpuResources);
    }

    /// <summary>
    ///     Creates the <see cref="PoseLandmarker" /> object from <paramref name="PoseLandmarkerOptions" />.
    /// </summary>
    /// <param name="options">Options for the pose landmarker task.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="PoseLandmarker" /> object that's created from <paramref name="options" />.
    /// </returns>
    public static PoseLandmarker CreateFromOptions(PoseLandmarkerOptions options, GpuResources? gpuResources = null)
    {
        List<string> outputStreams = new()
        {
            string.Join(":", _NORM_LANDMARKS_TAG, _NORM_LANDMARKS_STREAM_NAME),
            string.Join(":", _POSE_WORLD_LANDMARKS_TAG, _POSE_WORLD_LANDMARKS_STREAM_NAME),
            string.Join(":", _IMAGE_TAG, _IMAGE_OUT_STREAM_NAME)
        };
        if (options.OutputSegmentationMasks)
            outputStreams.Add(string.Join(":", _SEGMENTATION_MASK_TAG, _SEGMENTATION_MASK_STREAM_NAME));
        TaskInfo<PoseLandmarkerOptions> taskInfo = new(
            _TASK_GRAPH_NAME,
            [
                string.Join(":", _IMAGE_TAG, _IMAGE_IN_STREAM_NAME),
                string.Join(":", _NORM_RECT_TAG, _NORM_RECT_STREAM_NAME)
            ],
            outputStreams,
            options);

        return new PoseLandmarker(
            taskInfo.GenerateGraphConfig(options.RunningMode == VisionRunningMode.LIVE_STREAM),
            options.RunningMode,
            gpuResources,
            BuildPacketsCallback(options));
    }

    /// <summary>
    ///     Performs pose landmarks detection on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="PoseLandmarker" /> is created with the image running mode.
    ///     The image can be of any size with format RGB or RGBA.
    /// </summary>
    /// <param name="image">MediaPipe Image.</param>
    /// <param name="imageProcessingOptions">Options for image processing.</param>
    /// <returns>
    ///     The pose landmarks detection results.
    /// </returns>
    public PoseLandmarkerResult Detect(Image image, ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = DetectInternal(image, imageProcessingOptions);

        PoseLandmarkerResult result = default;
        _ = TryBuildPoseLandmarkerResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs pose landmarks detection on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="PoseLandmarker" /> is created with the image running mode.
    ///     The image can be of any size with format RGB or RGBA.
    /// </summary>
    /// <param name="image">MediaPipe Image.</param>
    /// <param name="imageProcessingOptions">Options for image processing.</param>
    /// <param name="result">
    ///     <see cref="PoseLandmarkerResult" /> to which the result will be written.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if some faces are detected, <see langword="false" /> otherwise.
    /// </returns>
    public bool TryDetect(Image image, ImageProcessingOptions? imageProcessingOptions, ref PoseLandmarkerResult result)
    {
        using PacketMap outputPackets = DetectInternal(image, imageProcessingOptions);
        return TryBuildPoseLandmarkerResult(outputPackets, ref result);
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
    ///     Performs pose landmarks detection on the provided video frames.
    ///     Only use this method when the PoseLandmarker is created with the video
    ///     running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <returns>
    ///     The pose landmarks detection results.
    /// </returns>
    public PoseLandmarkerResult DetectForVideo(Image image, long timestampMillisec,
        ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = DetectForVideoInternal(image, timestampMillisec, imageProcessingOptions);

        PoseLandmarkerResult result = default;
        _ = TryBuildPoseLandmarkerResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs pose landmarks detection on the provided video frames.
    ///     Only use this method when the PoseLandmarker is created with the video
    ///     running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <param name="result">
    ///     <see cref="PoseLandmarkerResult" /> to which the result will be written.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if some poses are detected, <see langword="false" /> otherwise.
    /// </returns>
    public bool TryDetectForVideo(Image image, long timestampMillisec, ImageProcessingOptions? imageProcessingOptions,
        ref PoseLandmarkerResult result)
    {
        using PacketMap outputPackets = DetectForVideoInternal(image, timestampMillisec, imageProcessingOptions);
        return TryBuildPoseLandmarkerResult(outputPackets, ref result);
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
    ///     Sends live image data to perform pose landmarks detection.
    ///     Only use this method when the PoseLandmarker is created with the live stream
    ///     running mode. The input timestamps should be monotonically increasing for
    ///     adjacent calls of this method. This method will return immediately after the
    ///     input image is accepted. The results will be available via the
    ///     <see cref="PoseLandmarkerOptions.ResultCallbackFunc" /> provided in the <see cref="PoseLandmarkerOptions" />.
    ///     The <see cref="DetectAsync" /> method is designed to process live stream data such as camera
    ///     input. To lower the overall latency, pose landmarker may drop the input
    ///     images if needed. In other words, it's not guaranteed to have output per
    ///     input image.
    public void DetectAsync(Image image, long timestampMillisec, ImageProcessingOptions? imageProcessingOptions = null)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);
        long timestampMicrosec = timestampMillisec * _MICRO_SECONDS_PER_MILLISECOND;

        PacketMap packetMap = new();
        packetMap.Emplace(_IMAGE_IN_STREAM_NAME, PacketHelper.CreateImageAt(image, timestampMicrosec));
        packetMap.Emplace(_NORM_RECT_STREAM_NAME, PacketHelper.CreateProtoAt(_normalizedRect, timestampMicrosec));

        SendLiveStreamData(packetMap);
    }

    private static TaskRunner.PacketsCallback? BuildPacketsCallback(PoseLandmarkerOptions options)
    {
        PoseLandmarkerOptions.ResultCallbackFunc? resultCallback = options.ResultCallback;
        if (resultCallback == null) return null;

        PoseLandmarkerResult poseLandmarkerResult =
            PoseLandmarkerResult.Alloc(options.NumPoses, options.OutputSegmentationMasks);

        return outputPackets =>
        {
            using Packet<Image>? outImagePacket = outputPackets.At<Image>(_IMAGE_OUT_STREAM_NAME);
            if (outImagePacket == null || outImagePacket.IsEmpty()) return;

            using Image image = outImagePacket.Get();
            long timestamp = outImagePacket.TimestampMicroseconds() / _MICRO_SECONDS_PER_MILLISECOND;

            if (TryBuildPoseLandmarkerResult(outputPackets, ref poseLandmarkerResult))
                resultCallback(poseLandmarkerResult, image, timestamp);
            else
                resultCallback(default, image, timestamp);
        };
    }

    private static bool TryBuildPoseLandmarkerResult(PacketMap outputPackets, ref PoseLandmarkerResult result)
    {
        using Packet<List<NormalizedLandmarks>> poseLandmarksPacket =
            outputPackets.At<List<NormalizedLandmarks>>(_NORM_LANDMARKS_STREAM_NAME);
        if (poseLandmarksPacket.IsEmpty()) return false;

        List<NormalizedLandmarks> poseLandmarks = result.PoseLandmarks ?? [];
        poseLandmarksPacket.Get(poseLandmarks);

        using Packet<List<Landmarks>> poseWorldLandmarksPacket =
            outputPackets.At<List<Landmarks>>(_POSE_WORLD_LANDMARKS_STREAM_NAME);
        List<Landmarks> poseWorldLandmarks = result.PoseWorldLandmarks ?? [];
        poseWorldLandmarksPacket.Get(poseWorldLandmarks);

        List<Image>? segmentationMasks = result.SegmentationMasks;
        using Packet<List<Image>>? segmentationMaskPacket =
            outputPackets.At<List<Image>>(_SEGMENTATION_MASK_STREAM_NAME);
        if (segmentationMaskPacket != null)
        {
            segmentationMasks ??= [];
            segmentationMaskPacket.Get(segmentationMasks);
        }

        result = new PoseLandmarkerResult(poseLandmarks, poseWorldLandmarks, segmentationMasks);
        return true;
    }
}