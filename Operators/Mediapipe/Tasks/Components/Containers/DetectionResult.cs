using Mediapipe.Extension;
using Mediapipe.TranMarshal;

namespace Mediapipe.Tasks.Components.Containers;

/// <summary>
///     Represents one detected object in the object detector's results.
/// </summary>
public readonly struct DetectionResultItem
{
    public const int DefaultCategoryIndex = -1;

    /// <summary>
    ///     A list of <see cref="Category" /> objects.
    /// </summary>
    public readonly List<Category> Categories;

    /// <summary>
    ///     The bounding box location.
    /// </summary>
    public readonly Rect BoundingBox;

    /// <summary>
    ///     Optional list of keypoints associated with the detection. Keypoints
    ///     represent interesting points related to the detection. For example, the
    ///     keypoints represent the eye, ear and mouth from face detection model. Or
    ///     in the template matching detection, e.g. KNIFT, they can represent the
    ///     feature points for template matching.
    /// </summary>
    public readonly List<NormalizedKeypoint>? Keypoints;

    internal DetectionResultItem(List<Category> categories, Rect boundingBox, List<NormalizedKeypoint>? keypoints)
    {
        Categories = categories;
        BoundingBox = boundingBox;
        Keypoints = keypoints;
    }

    public static DetectionResultItem CreateFrom(Detection proto)
    {
        DetectionResultItem result = default;

        Copy(proto, ref result);
        return result;
    }

    public static void Copy(Detection proto, ref DetectionResultItem destination)
    {
        List<Category> categories = destination.Categories ?? new List<Category>(proto.Score.Count);
        categories.Clear();
        for (int idx = 0; idx < proto.Score.Count; idx++)
            categories.Add(new Category(
                proto.LabelId.Count > idx ? proto.LabelId[idx] : DefaultCategoryIndex,
                proto.Score[idx],
                proto.Label.Count > idx ? proto.Label[idx] : "",
                proto.DisplayName.Count > idx ? proto.DisplayName[idx] : ""
            ));

        Rect boundingBox = proto.LocationData != null
            ? new Rect(
                proto.LocationData.BoundingBox.Xmin,
                proto.LocationData.BoundingBox.Ymin,
                proto.LocationData.BoundingBox.Xmin + proto.LocationData.BoundingBox.Width,
                proto.LocationData.BoundingBox.Ymin + proto.LocationData.BoundingBox.Height
            )
            : new Rect(0, 0, 0, 0);

        if (proto.LocationData?.RelativeKeypoints.Count == 0)
        {
            destination = new DetectionResultItem(categories, boundingBox, null);
            return;
        }

        List<NormalizedKeypoint> keypoints = destination.Keypoints ??
                                             new List<NormalizedKeypoint>(proto.LocationData?.RelativeKeypoints.Count ??
                                                                          0);
        keypoints.Clear();
        for (int i = 0; i < proto.LocationData?.RelativeKeypoints.Count; i++)
        {
            LocationData.Types.RelativeKeypoint? keypoint = proto.LocationData.RelativeKeypoints[i];
            keypoints.Add(new NormalizedKeypoint(
                keypoint.X,
                keypoint.Y,
                keypoint.HasKeypointLabel ? keypoint.KeypointLabel : null!,
                keypoint.HasScore ? keypoint.Score : null
            ));
        }

        destination = new DetectionResultItem(categories, boundingBox, keypoints);
    }

    internal static void Copy(in NativeDetection source, ref DetectionResultItem destination)
    {
        List<Category> categories = destination.Categories ?? new List<Category>((int)source.categoriesCount);
        categories.Clear();
        foreach (NativeCategory nativeCategory in source.Categories) categories.Add(new Category(nativeCategory));

        Rect boundingBox = new(source.boundingBox);

        List<NormalizedKeypoint> keypoints =
            destination.Keypoints ?? new List<NormalizedKeypoint>((int)source.keypointsCount);
        keypoints.Clear();
        foreach (NativeNormalizedKeypoint nativeKeypoint in source.Keypoints)
            keypoints.Add(new NormalizedKeypoint(nativeKeypoint));

        destination = new DetectionResultItem(categories, boundingBox, keypoints);
    }

    public override string ToString()
    {
        return
            $"{{ \"categories\": {Util.Format(Categories)}, \"boundingBox\": {BoundingBox}, \"keypoints\": {Util.Format(Keypoints)} }}";
    }
}

/// <summary>
///     Represents the list of detected objects.
/// </summary>
public readonly struct DetectionResult
{
    /// <summary>
    ///     A list of <see cref="DetectionResultItem" /> objects.
    /// </summary>
    public readonly List<DetectionResultItem> Detections;

    internal DetectionResult(List<DetectionResultItem> detections)
    {
        Detections = detections;
    }

    public static DetectionResult Alloc(int capacity)
    {
        return new DetectionResult(new List<DetectionResultItem>(capacity));
    }

    public static DetectionResult CreateFrom(List<Detection> detectionsProto)
    {
        DetectionResult result = default;

        Copy(detectionsProto, ref result);
        return result;
    }

    public static void Copy(List<Detection> source, ref DetectionResult destination)
    {
        List<DetectionResultItem> detections = destination.Detections ?? new List<DetectionResultItem>(source.Count);
        detections.ResizeTo(source.Count);

        for (int i = 0; i < source.Count; i++)
        {
            DetectionResultItem detection = detections[i];
            DetectionResultItem.Copy(source[i], ref detection);
            detections[i] = detection;
        }

        destination = new DetectionResult(detections);
    }

    internal static void Copy(NativeDetectionResult source, ref DetectionResult destination)
    {
        List<DetectionResultItem> detections =
            destination.Detections ?? new List<DetectionResultItem>((int)source.detectionsCount);
        detections.ResizeTo((int)source.detectionsCount);

        int i = 0;
        foreach (NativeDetection nativeDetection in source.AsReadOnlySpan())
        {
            DetectionResultItem detection = detections[i];
            DetectionResultItem.Copy(nativeDetection, ref detection);
            detections[i++] = detection;
        }

        destination = new DetectionResult(detections);
    }

    public override string ToString()
    {
        return $"{{ \"detections\": {Util.Format(Detections)} }}";
    }
}