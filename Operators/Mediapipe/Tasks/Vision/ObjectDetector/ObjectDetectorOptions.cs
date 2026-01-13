using Mediapipe.Framework.Formats;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Core.Proto;
using Mediapipe.Tasks.Vision.Core;

namespace Mediapipe.Tasks.Vision.ObjectDetector;

/// <summary>
///     Options for the object detector task.
/// </summary>
public sealed class ObjectDetectorOptions(
    CoreBaseOptions baseOptions,
    VisionRunningMode runningMode = VisionRunningMode.IMAGE,
    string? displayNamesLocale = null,
    int? maxResults = null,
    float? scoreThreshold = null,
    List<string>? categoryAllowList = null,
    List<string>? categoryDenyList = null,
    ObjectDetectorOptions.ResultCallbackFunc? resultCallback = null) : ITaskOptions
{
    /// <remarks>
    ///     Some field of <paramref name="detectionResult" /> can be reused to reduce GC.Alloc.
    ///     If you need to refer to the data later, copy the data.
    /// </remarks>
    /// <param name="detectionResult">
    ///     A detection result object that contains a list of detections,
    ///     each detection has a bounding box that is expressed in the unrotated
    ///     input frame of reference coordinates system,
    ///     i.e. in `[0,image_width) x [0,image_height)`, which are the dimensions
    ///     of the underlying image data.
    /// </param>
    /// <param name="image">
    ///     The input image that the object detector runs on.
    /// </param>
    /// <param name="timestampMillisec">
    ///     The input timestamp in milliseconds.
    /// </param>
    public delegate void ResultCallbackFunc(DetectionResult detectionResult, Image image, long timestampMillisec);

    /// <summary>
    ///     Base options for the object detector task.
    /// </summary>
    public CoreBaseOptions BaseOptions { get; } = baseOptions;

    /// <summary>
    ///     The running mode of the task. Default to the image mode.
    ///     Object detector task has three running modes:
    ///     <list type="number">
    ///         <item>
    ///             <description>The image mode for detecting objects on single image inputs.</description>
    ///         </item>
    ///         <item>
    ///             <description>The video mode for detecting objects on the decoded frames of a video.</description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 The live stream mode or detecting objects on the live stream of input data, such as from camera.
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
    ///     The maximum number of top-scored classification results to return.
    /// </summary>
    public int? MaxResults { get; } = maxResults;

    /// <summary>
    ///     Overrides the ones provided in the model metadata. Results below this value are rejected.
    /// </summary>
    public float? ScoreThreshold { get; } = scoreThreshold;

    /// <summary>
    ///     Allowlist of category names.
    ///     If non-empty, classification results whose category name is not in this set will be filtered out.
    ///     Duplicate or unknown category names are ignored. Mutually exclusive with <see cref="categoryDenylist" />.
    /// </summary>
    public List<string>? CategoryAllowList { get; } = categoryAllowList;

    /// <summary>
    ///     Denylist of category names.
    ///     If non-empty, classification results whose category name is in this set will be filtered out.
    ///     Duplicate or unknown category names are ignored. Mutually exclusive with <see cref="CategoryAllowList" />.
    /// </summary>
    public List<string>? CategoryDenyList { get; } = categoryDenyList;

    /// <summary>
    ///     The user-defined result callback for processing live stream data.
    ///     The result callback should only be specified when the running mode is set to the live stream mode.
    /// </summary>
    public ResultCallbackFunc? ResultCallback { get; } = resultCallback;

    CalculatorOptions ITaskOptions.ToCalculatorOptions()
    {
        CalculatorOptions options = new();
        options.SetExtension(Proto.ObjectDetectorOptions.Extensions.Ext, ToProto());
        return options;
    }

    internal Proto.ObjectDetectorOptions ToProto()
    {
        BaseOptions baseOptionsProto = BaseOptions.ToProto();
        baseOptionsProto.UseStreamMode = RunningMode != VisionRunningMode.IMAGE;

        Proto.ObjectDetectorOptions options = new()
        {
            BaseOptions = baseOptionsProto
        };

        if (DisplayNamesLocale != null) options.DisplayNamesLocale = DisplayNamesLocale;
        if (MaxResults is int maxResultsValue) options.MaxResults = maxResultsValue;
        if (ScoreThreshold is float scoreThresholdValue) options.ScoreThreshold = scoreThresholdValue;
        if (CategoryAllowList != null) options.CategoryAllowlist.AddRange(CategoryAllowList);
        if (CategoryDenyList != null) options.CategoryDenylist.AddRange(CategoryDenyList);

        return options;
    }
}