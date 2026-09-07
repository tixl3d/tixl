namespace T3.Editor.Gui.UiHelpers.DelaunayVoronoi;

public sealed class Point
{
    /// <summary>
    /// Used only for generating a unique ID for each instance of this class that gets generated
    /// </summary>
    private static int _counter;

    /// <summary>
    /// Used for identifying an instance of a class; can be useful in troubleshooting when geometry goes weird
    /// (e.g. when trying to identify when Triangle objects are being created with the same Point object twice)
    /// </summary>
    private readonly int _instanceId = _counter++;

    public double X { get; }
    public double Y { get; }
    public HashSet<Triangle> AdjacentTriangles { get; } = new();

    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }

    public override string ToString()
    {
        // Simple way of seeing what's going on in the debugger when investigating weirdness
        return $"{nameof(Point)} {_instanceId} {X:0.##}@{Y:0.##}";
    }

    public Vector2 ToVec2()
    {
        return new Vector2((float)X, (float)Y);
    }
        
    public static Point FromVec2(Vector2 v)
    {
        return new Point(v.X, v.Y);
    } 

}