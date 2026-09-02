using System.Runtime.InteropServices;

namespace T3.Core.DataTypes;

[StructLayout(LayoutKind.Explicit, Size = Stride)]
public struct Point
{
    [FieldOffset(0)]
    public Vector3 Position;

    [FieldOffset(3 * 4)]
    public float F1;

    [FieldOffset(4 * 4)]
    public Quaternion Orientation;

    [FieldOffset(8 * 4)]
    public Vector4 Color;
        
    [FieldOffset(12 * 4)]
    public Vector3 Scale;
        
    [FieldOffset(15 * 4)]
    public float F2;


    public Point()
    {
        Position = Vector3.Zero;
        F1 = 1;
        Orientation = Quaternion.Identity;
        Color = Vector4.One;
        Scale = Vector3.One;
        F2 = 1;
    }

    public static Point Separator()
    {
        return new Point
                   {
                       Position = Vector3.Zero,
                       F1 = 1,
                       Orientation = Quaternion.Identity,
                       Color = Vector4.One,
                       Scale = new Vector3(float.NaN, float.NaN, float.NaN),
                       F2 = 1,
                   };
    }

    /// <summary>
    /// A point with NaN Scale.X is a separator: it is never drawn and line-style
    /// rendering breaks its strip at it. This is the only NaN convention for points
    /// (shader-side counterpart: IsSeparator() in shared/point.hlsl).
    /// </summary>
    public static bool IsSeparator(in Point point)
    {
        return float.IsNaN(point.Scale.X);
    }

    [Newtonsoft.Json.JsonIgnore]
    public const int Stride = 16 * 4;
}

/// <summary>
/// A consecutive run of non-separator points within a point list, e.g. one polyline contour.
/// </summary>
public readonly struct PointSegment
{
    public PointSegment(int start, int count)
    {
        Start = start;
        Count = count;
    }

    public readonly int Start;
    public readonly int Count;
}

/// <summary>
/// Enumerates the <see cref="PointSegment"/>s of a point list, skipping separator points.
/// Allocation-free: foreach (var segment in PointSegments.Of(points)) ...
/// </summary>
public readonly struct PointSegments
{
    private PointSegments(Point[] points, int count)
    {
        _points = points;
        _count = count;
    }

    public static PointSegments Of(Point[] points) => new(points, points.Length);
    public static PointSegments Of(StructuredList<Point> list) => new(list.TypedElements, list.NumElements);

    public Enumerator GetEnumerator() => new(_points, _count);

    public struct Enumerator
    {
        internal Enumerator(Point[] points, int count)
        {
            _points = points;
            _count = count;
            _index = 0;
        }

        public PointSegment Current { get; private set; }

        public bool MoveNext()
        {
            var start = _index;
            while (start < _count && Point.IsSeparator(in _points[start]))
            {
                start++;
            }

            if (start >= _count)
                return false;

            var end = start;
            while (end < _count && !Point.IsSeparator(in _points[end]))
            {
                end++;
            }

            Current = new PointSegment(start, end - start);
            _index = end;
            return true;
        }

        private readonly Point[] _points;
        private readonly int _count;
        private int _index;
    }

    private readonly Point[] _points;
    private readonly int _count;
}