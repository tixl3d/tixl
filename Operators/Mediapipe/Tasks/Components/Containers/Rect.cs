using Mediapipe.TranMarshal;

namespace Mediapipe.Tasks.Components.Containers;

/// <summary>
///     Defines a rectangle, used e.g. as part of detection results or as input region-of-interest.
/// </summary>
public readonly struct Rect
{
    public readonly int Left;
    public readonly int Top;
    public readonly int Right;
    public readonly int Bottom;

    internal Rect(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    internal Rect(NativeRect nativeRect) : this(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom)
    {
    }

    public override string ToString()
    {
        return $"{{ \"left\": {Left}, \"top\": {Top}, \"right\": {Right}, \"bottom\": {Bottom} }}";
    }
}

/// <summary>
///     A rectangle, used as part of detection results or as input region-of-interest.
///     The coordinates are normalized wrt the image dimensions, i.e. generally in
///     [0,1] but they may exceed these bounds if describing a region overlapping the
///     image. The origin is on the top-left corner of the image.
/// </summary>
public readonly struct RectF : IEquatable<RectF>
{
    private const float _RectFTolerance = 1e-4f;

    public readonly float left;
    public readonly float top;
    public readonly float right;
    public readonly float bottom;

    internal RectF(float left, float top, float right, float bottom)
    {
        this.left = left;
        this.top = top;
        this.right = right;
        this.bottom = bottom;
    }

    internal RectF(NativeRectF nativeRect) : this(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom)
    {
    }

    public override bool Equals(object? obj)
    {
        return obj is RectF other && Equals(other);
    }

    bool IEquatable<RectF>.Equals(RectF other)
    {
        return MathF.Abs(left - other.left) < _RectFTolerance &&
               MathF.Abs(top - other.top) < _RectFTolerance &&
               MathF.Abs(right - other.right) < _RectFTolerance &&
               MathF.Abs(bottom - other.bottom) < _RectFTolerance;
    }

    // TODO: use HashCode.Combine
    public override int GetHashCode()
    {
        return Tuple.Create(left, top, right, bottom).GetHashCode();
    }

    public static bool operator ==(RectF lhs, RectF rhs)
    {
        return lhs.Equals(rhs);
    }

    public static bool operator !=(RectF lhs, RectF rhs)
    {
        return !(lhs == rhs);
    }

    public override string ToString()
    {
        return $"{{ \"left\": {left}, \"top\": {top}, \"right\": {right}, \"bottom\": {bottom} }}";
    }
}