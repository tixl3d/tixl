using Mediapipe.Framework;
using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Packet;
using Mediapipe.Gpu;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;

namespace Mediapipe.Tasks.Vision.ImageSegmenter;

public sealed class ImageSegmenter : BaseVisionTaskApi
{
    private const string _CONFIDENCE_MASKS_STREAM_NAME = "confidence_masks";
    private const string _CONFIDENCE_MASKS_TAG = "CONFIDENCE_MASKS";
    private const string _CATEGORY_MASK_STREAM_NAME = "category_mask";
    private const string _CATEGORY_MASK_TAG = "CATEGORY_MASK";
    private const string _IMAGE_IN_STREAM_NAME = "image_in";
    private const string _IMAGE_OUT_STREAM_NAME = "image_out";
    private const string _IMAGE_TAG = "IMAGE";
    private const string _NORM_RECT_STREAM_NAME = "norm_rect_in";
    private const string _NORM_RECT_TAG = "NORM_RECT";
    private const string _TENSORS_TO_SEGMENTATION_CALCULATOR_NAME = "mediapipe.tasks.TensorsToSegmentationCalculator";
    private const string _TASK_GRAPH_NAME = "mediapipe.tasks.vision.image_segmenter.ImageSegmenterGraph";

    private const int _MICRO_SECONDS_PER_MILLISECOND = 1000;

    private readonly Lazy<List<string>> _labels;

    private readonly NormalizedRect _normalizedRect = new();

    private ImageSegmenter(
        CalculatorGraphConfig graphConfig,
        VisionRunningMode runningMode,
        GpuResources? gpuResources,
        TaskRunner.PacketsCallback? packetCallback) : base(graphConfig, runningMode, gpuResources, packetCallback)
    {
        _labels = new Lazy<List<string>>(GetLabels);
    }

    public IReadOnlyList<string> Labels => _labels.Value;

    /// <summary>
    ///     Creates an <see cref="ImageSegmenter" /> object from a TensorFlow Lite model and the default
    ///     <see cref="ImageSegmenterOptions" />.
    ///     Note that the created <see cref="ImageSegmenter" /> instance is in image mode,
    ///     for performing image segmentation on single image inputs.
    /// </summary>
    /// <param name="modelPath">Path to the model.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="ImageSegmenter" /> object that's created from the model and the default
    ///     <see cref="ImageSegmenterOptions" />.
    /// </returns>
    public static ImageSegmenter CreateFromModelPath(string modelPath, GpuResources? gpuResources = null)
    {
        CoreBaseOptions baseOptions = new(modelAssetPath: modelPath);
        ImageSegmenterOptions options = new(baseOptions);
        return CreateFromOptions(options, gpuResources);
    }

    /// <summary>
    ///     Creates the <see cref="ImageSegmenter" /> object from <paramref name="ImageSegmenterOptions" />.
    /// </summary>
    /// <param name="options">Options for the image segmenter task.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="ImageSegmenter" /> object that's created from <paramref name="options" />.
    /// </returns>
    public static ImageSegmenter CreateFromOptions(ImageSegmenterOptions options, GpuResources? gpuResources = null)
    {
        List<string> outputStreams = new()
        {
            string.Join(":", _IMAGE_TAG, _IMAGE_OUT_STREAM_NAME)
        };

        if (options.OutputConfidenceMasks)
            outputStreams.Add(string.Join(":", _CONFIDENCE_MASKS_TAG, _CONFIDENCE_MASKS_STREAM_NAME));
        if (options.OutputCategoryMask)
            outputStreams.Add(string.Join(":", _CATEGORY_MASK_TAG, _CATEGORY_MASK_STREAM_NAME));

        TaskInfo<ImageSegmenterOptions> taskInfo = new(
            _TASK_GRAPH_NAME,
            [
                string.Join(":", _IMAGE_TAG, _IMAGE_IN_STREAM_NAME),
                string.Join(":", _NORM_RECT_TAG, _NORM_RECT_STREAM_NAME)
            ],
            outputStreams,
            options);

        return new ImageSegmenter(
            taskInfo.GenerateGraphConfig(options.RunningMode == VisionRunningMode.LIVE_STREAM),
            options.RunningMode,
            gpuResources,
            BuildPacketsCallback(options));
    }

    /// <summary>
    ///     Performs the actual segmentation task on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="ImageSegmenter" /> is created with the image running mode.
    /// </summary>
    /// <param name="image">MediaPipe Image.</param>
    /// <param name="imageProcessingOptions">Options for image processing.</param>
    /// <returns>
    ///     If the output_type is CATEGORY_MASK, the returned vector of images is per-category segmented image mask.
    ///     If the output_type is CONFIDENCE_MASK, the returned vector of images contains only one confidence image mask.
    ///     A segmentation result object that contains a list of segmentation masks as images.
    /// </returns>
    public ImageSegmenterResult Segment(Image image, ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = SegmentInternal(image, imageProcessingOptions);

        ImageSegmenterResult result = default;
        _ = TryBuildImageSegmenterResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs the actual segmentation task on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="ImageSegmenter" /> is created with the image running mode.
    /// </summary>
    /// <param name="image">MediaPipe Image.</param>
    /// <param name="imageProcessingOptions">Options for image processing.</param>
    /// <param name="result">
    ///     <see cref="ImageSegmenterResult" /> to which the result will be written.
    ///     If the output_type is CATEGORY_MASK, the returned vector of images is per-category segmented image mask.
    ///     If the output_type is CONFIDENCE_MASK, the returned vector of images contains only one confidence image mask.
    ///     A segmentation result object that contains a list of segmentation masks as images.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if the segmentation is successful, <see langword="false" /> otherwise.
    /// </returns>
    public bool TrySegment(Image image, ImageProcessingOptions? imageProcessingOptions, ref ImageSegmenterResult result)
    {
        using PacketMap outputPackets = SegmentInternal(image, imageProcessingOptions);
        return TryBuildImageSegmenterResult(outputPackets, ref result);
    }

    private PacketMap SegmentInternal(Image image, ImageProcessingOptions? imageProcessingOptions)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);

        PacketMap packetMap = new();
        packetMap.Emplace(_IMAGE_IN_STREAM_NAME, PacketHelper.CreateImage(image));
        packetMap.Emplace(_NORM_RECT_STREAM_NAME, PacketHelper.CreateProto(_normalizedRect));

        return ProcessImageData(packetMap);
    }

    /// <summary>
    ///     Performs segmentation on the provided video frames.
    ///     Only use this method when the ImageSegmenter is created with the video
    ///     running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <returns>
    ///     If the output_type is CATEGORY_MASK, the returned vector of images is per-category segmented image mask.
    ///     If the output_type is CONFIDENCE_MASK, the returned vector of images contains only one confidence image mask.
    ///     A segmentation result object that contains a list of segmentation masks as images.
    /// </returns>
    public ImageSegmenterResult SegmentForVideo(Image image, long timestampMillisec,
        ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = SegmentForVideoInternal(image, timestampMillisec, imageProcessingOptions);

        ImageSegmenterResult result = default;
        _ = TryBuildImageSegmenterResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs segmentation on the provided video frames.
    ///     Only use this method when the ImageSegmenter is created with the video
    ///     running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <param name="result">
    ///     <see cref="ImageSegmenterResult" /> to which the result will be written.
    ///     If the output_type is CATEGORY_MASK, the returned vector of images is per-category segmented image mask.
    ///     If the output_type is CONFIDENCE_MASK, the returned vector of images contains only one confidence image mask.
    ///     A segmentation result object that contains a list of segmentation masks as images.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if the segmentation is successful, <see langword="false" /> otherwise.
    /// </returns>
    public bool TrySegmentForVideo(Image image, long timestampMillisec, ImageProcessingOptions? imageProcessingOptions,
        ref ImageSegmenterResult result)
    {
        using PacketMap outputPackets = SegmentForVideoInternal(image, timestampMillisec, imageProcessingOptions);
        return TryBuildImageSegmenterResult(outputPackets, ref result);
    }

    private PacketMap SegmentForVideoInternal(Image image, long timestampMillisec,
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
    ///     Sends live image data (an Image with a unique timestamp) to perform image segmentation.
    ///     Only use this method when the ImageSegmenter is created with the live stream
    ///     running mode. The input timestamps should be monotonically increasing for
    ///     adjacent calls of this method. This method will return immediately after the
    ///     input image is accepted. The results will be available via the
    ///     <see cref="ImageSegmenterOptions.ResultCallbackFunc" /> provided in the <see cref="ImageSegmenterOptions" />.
    ///     The <see cref="SegmentAsync" /> method is designed to process live stream data such as camera
    ///     input. To lower the overall latency, image segmenter may drop the input
    ///     images if needed. In other words, it's not guaranteed to have output per
    ///     input image.
    public void SegmentAsync(Image image, long timestampMillisec, ImageProcessingOptions? imageProcessingOptions = null)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);
        long timestampMicrosec = timestampMillisec * _MICRO_SECONDS_PER_MILLISECOND;

        PacketMap packetMap = new();
        packetMap.Emplace(_IMAGE_IN_STREAM_NAME, PacketHelper.CreateImageAt(image, timestampMicrosec));
        packetMap.Emplace(_NORM_RECT_STREAM_NAME, PacketHelper.CreateProtoAt(_normalizedRect, timestampMicrosec));

        SendLiveStreamData(packetMap);
    }

    private static TaskRunner.PacketsCallback? BuildPacketsCallback(ImageSegmenterOptions options)
    {
        ImageSegmenterOptions.ResultCallbackFunc? resultCallback = options.ResultCallback;
        if (resultCallback == null) return null;

        ImageSegmenterResult segmentationResult = ImageSegmenterResult.Alloc(options.OutputConfidenceMasks);

        return outputPackets =>
        {
            using Packet<Image>? outImagePacket = outputPackets.At<Image>(_IMAGE_OUT_STREAM_NAME);
            if (outImagePacket == null || outImagePacket.IsEmpty()) return;

            using Image image = outImagePacket.Get();
            long timestamp = outImagePacket.TimestampMicroseconds() / _MICRO_SECONDS_PER_MILLISECOND;

            if (TryBuildImageSegmenterResult(outputPackets, ref segmentationResult))
                resultCallback(segmentationResult, image, timestamp);
            else
                resultCallback(default, image, timestamp);
        };
    }

    private static bool TryBuildImageSegmenterResult(PacketMap outputPackets, ref ImageSegmenterResult result)
    {
        bool found = false;
        List<Image>? confidenceMasks = null;
        if (outputPackets.TryGet<List<Image>>(_CONFIDENCE_MASKS_STREAM_NAME,
                out Packet<List<Image>> confidenceMasksPacket))
        {
            found = true;
            confidenceMasks = result.ConfidenceMasks ?? [];
            confidenceMasksPacket.Get(confidenceMasks);
            confidenceMasksPacket.Dispose();
        }

        Image? categoryMask = null;
        if (outputPackets.TryGet<Image>(_CATEGORY_MASK_STREAM_NAME, out Packet<Image> categoryMaskPacket))
        {
            found = true;
            categoryMask = categoryMaskPacket.Get();
            categoryMaskPacket.Dispose();
        }

        if (!found) return false;
        result = new ImageSegmenterResult(confidenceMasks, categoryMask);
        return true;
    }

    private List<string> GetLabels()
    {
        CalculatorGraphConfig graphConfig = GetGraphConfig();
        List<string> labels = new();

        foreach (CalculatorGraphConfig.Types.Node? node in graphConfig.Node)
            if (node.Name.EndsWith(_TENSORS_TO_SEGMENTATION_CALCULATOR_NAME))
            {
                TensorsToSegmentationCalculatorOptions? options =
                    node.Options.GetExtension(TensorsToSegmentationCalculatorOptions.Extensions.Ext);
                if (options?.LabelItems?.Count > 0)
                {
                    foreach (KeyValuePair<long, LabelMapItem> labelItem in options.LabelItems)
                        labels.Add(labelItem.Value.Name);
                    return labels;
                }
            }

        return labels;
    }
}