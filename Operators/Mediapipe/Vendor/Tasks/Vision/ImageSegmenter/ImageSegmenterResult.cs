using Mediapipe.Framework.Formats;

namespace Mediapipe.Tasks.Vision.ImageSegmenter;

/// <summary>
///     Output result of ImageSegmenter.
/// </summary>
public readonly struct ImageSegmenterResult
{
    /// <summary>
    ///     multiple masks of float image where, for each mask,
    ///     each pixel represents the prediction confidence, usually in the [0, 1] range.
    /// </summary>
    public readonly List<Image>? ConfidenceMasks;

    /// <summary>
    ///     a category mask of uint8 image where each pixel represents the class
    ///     which the pixel in the original image was predicted to belong to.
    /// </summary>
    public readonly Image? CategoryMask;

    internal ImageSegmenterResult(List<Image>? confidenceMasks, Image? categoryMask)
    {
        ConfidenceMasks = confidenceMasks;
        CategoryMask = categoryMask;
    }

    public static ImageSegmenterResult Alloc(bool outputConfidenceMasks = false)
    {
        List<Image>? confidenceMasks = outputConfidenceMasks ? new List<Image>() : null;
        return new ImageSegmenterResult(confidenceMasks, null);
    }

    public void CloneTo(ref ImageSegmenterResult destination)
    {
        List<Image>? dstConfidenceMasks = destination.ConfidenceMasks;
        dstConfidenceMasks?.Clear();
        if (ConfidenceMasks != null)
        {
            dstConfidenceMasks ??= new List<Image>(ConfidenceMasks.Count);
            dstConfidenceMasks.Clear();
            dstConfidenceMasks.AddRange(ConfidenceMasks);
        }

        destination = new ImageSegmenterResult(dstConfidenceMasks, CategoryMask);
    }
}