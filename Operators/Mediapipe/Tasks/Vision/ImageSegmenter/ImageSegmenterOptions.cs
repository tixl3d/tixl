using Mediapipe.Framework.Formats;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Core.Proto;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.ImageSegmenter.Proto;

namespace Mediapipe.Tasks.Vision.ImageSegmenter;

/// <summary>
///     Options for the image segmenter task.
/// </summary>
public sealed class ImageSegmenterOptions(
    CoreBaseOptions baseOptions,
    VisionRunningMode runningMode = VisionRunningMode.IMAGE,
    string? displayNamesLocale = null,
    bool outputConfidenceMasks = true,
    bool outputCategoryMask = false,
    ImageSegmenterOptions.ResultCallbackFunc? resultCallback = null) : ITaskOptions
{
    /// <remarks>
    ///     Some field of <paramref name="segmentationResult" /> can be reused to reduce GC.Alloc.
    ///     If you need to refer to the data later, copy the data.
    /// </remarks>
    /// <param name="segmentationResult">
    ///     A segmentation result object that contains a list of segmentation masks as images.
    /// </param>
    /// <param name="image">
    ///     The input image that the image segmenter runs on.
    /// </param>
    /// <param name="timestampMillisec">
    ///     The input timestamp in milliseconds.
    /// </param>
    public delegate void ResultCallbackFunc(ImageSegmenterResult segmentationResult, Image image,
        long timestampMillisec);

    /// <summary>
    ///     Base options for the image segmenter task.
    /// </summary>
    public CoreBaseOptions BaseOptions { get; } = baseOptions;

    /// <summary>
    ///     The running mode of the task. Default to the image mode.
    ///     image segmenter task has three running modes:
    ///     <list type="number">
    ///         <item>
    ///             <description>The image mode for segmenting objects on single image inputs.</description>
    ///         </item>
    ///         <item>
    ///             <description>The video mode for segmenting objects on the decoded frames of a video.</description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 The live stream mode or segmenting objects on the live stream of input data, such as from camera.
    ///             </description>
    ///         </item>
    ///     </list>
    /// </summary>
    public VisionRunningMode RunningMode { get; } = runningMode;

    /// <summary>
    ///     The locale to use for display names specified through the TFLite Model Metadata.
    /// </summary>
    public string? DisplayNamesLocale { get; } = displayNamesLocale;

    /// <summary>
    ///     Whether to output confidence masks.
    /// </summary>
    public bool OutputConfidenceMasks { get; } = outputConfidenceMasks;

    /// <summary>
    ///     Whether to output category mask.
    /// </summary>
    public bool OutputCategoryMask { get; } = outputCategoryMask;

    /// <summary>
    ///     The user-defined result callback for processing live stream data.
    ///     The result callback should only be specified when the running mode is set to the live stream mode.
    /// </summary>
    public ResultCallbackFunc? ResultCallback { get; } = resultCallback;

    CalculatorOptions ITaskOptions.ToCalculatorOptions()
    {
        CalculatorOptions options = new();
        options.SetExtension(ImageSegmenterGraphOptions.Extensions.Ext, ToProto());
        return options;
    }

    internal ImageSegmenterGraphOptions ToProto()
    {
        BaseOptions baseOptionsProto = BaseOptions.ToProto();
        baseOptionsProto.UseStreamMode = RunningMode != VisionRunningMode.IMAGE;

        ImageSegmenterGraphOptions options = new()
        {
            BaseOptions = baseOptionsProto
        };

        if (DisplayNamesLocale != null) options.DisplayNamesLocale = DisplayNamesLocale;

        return options;
    }
}