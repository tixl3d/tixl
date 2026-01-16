using Mediapipe.TranMarshal;

namespace Mediapipe.Tasks.Components.Containers;

/// <summary>
///     Defines a single classification result.
///     The label maps packed into the TFLite Model Metadata [1] are used to populate
///     the 'category_name' and 'display_name' fields.
///     [1]: https://www.tensorflow.org/lite/convert/metadata
/// </summary>
public readonly struct Category
{
    /// <summary>
    ///     The index of the category in the classification model output.
    /// </summary>
    public readonly int Index;

    /// <summary>
    ///     The score for this category, e.g. (but not necessarily) a probability in [0,1].
    /// </summary>
    public readonly float Score;

    /// <summary>
    ///     The optional ID for the category, read from the label map packed in the
    ///     TFLite Model Metadata if present. Not necessarily human-readable.
    /// </summary>
    public readonly string? CategoryName;

    /// <summary>
    ///     The optional human-readable name for the category, read from the label map
    ///     packed in the TFLite Model Metadata if present.
    /// </summary>
    public readonly string? DisplayName;

    internal Category(int index, float score, string? categoryName, string? displayName)
    {
        Index = index;
        Score = score;
        CategoryName = categoryName;
        DisplayName = displayName;
    }

    internal Category(NativeCategory nativeCategory) : this(
        nativeCategory.index,
        nativeCategory.score,
        nativeCategory.CategoryName,
        nativeCategory.DisplayName)
    {
    }

    public static Category CreateFrom(Classification proto)
    {
        string? categoryName = proto.HasLabel ? proto.Label : null;
        string? displayName = proto.HasDisplayName ? proto.DisplayName : null;
        return new Category(proto.Index, proto.Score, categoryName, displayName);
    }

    public override string ToString()
    {
        return
            $"{{ \"index\": {Index}, \"score\": {Score}, \"categoryName\": \"{CategoryName}\", \"displayName\": \"{DisplayName}\" }}";
    }
}