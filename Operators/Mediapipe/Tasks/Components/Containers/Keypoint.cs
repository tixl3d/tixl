using Mediapipe.TranMarshal;

namespace Mediapipe.Tasks.Components.Containers;

/// <summary>
///     A keypoint, defined by the coordinates (x, y), normalized by the image dimensions.
/// </summary>
public readonly struct NormalizedKeypoint
{
    /// <summary>
    ///     x in normalized image coordinates.
    /// </summary>
    public readonly float X;

    /// <summary>
    ///     y in normalized image coordinates.
    /// </summary>
    public readonly float Y;

    /// <summary>
    ///     optional label of the keypoint.
    /// </summary>
    public readonly string? Label;

    /// <summary>
    ///     optional score of the keypoint.
    /// </summary>
    public readonly float? Score;

    internal NormalizedKeypoint(float x, float y, string? label, float? score)
    {
        X = x;
        Y = y;
        Label = label;
        Score = score;
    }

    internal NormalizedKeypoint(NativeNormalizedKeypoint nativeKeypoint) : this(
        nativeKeypoint.x,
        nativeKeypoint.y,
        nativeKeypoint.Label,
        nativeKeypoint.hasScore ? nativeKeypoint.score : null)
    {
    }

    public override string ToString()
    {
        return $"{{ \"x\": {X}, \"y\": {Y}, \"label\": \"{Label}\", \"score\": {Util.Format(Score)} }}";
    }
}