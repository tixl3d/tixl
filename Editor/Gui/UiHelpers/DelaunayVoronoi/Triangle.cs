namespace T3.Editor.Gui.UiHelpers.DelaunayVoronoi;

public sealed class Triangle
{
    public Point[] Vertices { get; } = new Point[3];
    public Point Circumcenter { get; private set; }
    public double RadiusSquared;

    public IEnumerable<Triangle> TrianglesWithSharedEdge {
        get {
            var neighbors = new HashSet<Triangle>();
            foreach (var vertex in Vertices)
            {
                var trianglesWithSharedEdge = vertex.AdjacentTriangles.Where(o =>
                                                                             {
                                                                                 return o != this && SharesEdgeWith(o);
                                                                             });
                neighbors.UnionWith(trianglesWithSharedEdge);
            }

            return neighbors;
        }
    }

    public Triangle(Point point1, Point point2, Point point3)
    {
        // In theory this shouldn't happen, but it was at one point so this at least makes sure we're getting a
        // relatively easily-recognised error message, and provides a handy breakpoint for debugging.
        if (point1 == point2 || point1 == point3 || point2 == point3)
        {
            throw new ArgumentException("Must be 3 distinct points");
        }

        if (!IsCounterClockwise(point1, point2, point3))
        {
            Vertices[0] = point1;
            Vertices[1] = point3;
            Vertices[2] = point2;
        }
        else
        {
            Vertices[0] = point1;
            Vertices[1] = point2;
            Vertices[2] = point3;
        }

        Vertices[0].AdjacentTriangles.Add(this);
        Vertices[1].AdjacentTriangles.Add(this);
        Vertices[2].AdjacentTriangles.Add(this);
        UpdateCircumcircle();
    }

    private void UpdateCircumcircle()
    {
        // https://codefound.wordpress.com/2013/02/21/how-to-compute-a-circumcircle/#more-58
        // https://en.wikipedia.org/wiki/Circumscribed_circle
        var p0 = Vertices[0];
        var p1 = Vertices[1];
        var p2 = Vertices[2];
        var dA = p0.X * p0.X + p0.Y * p0.Y;
        var dB = p1.X * p1.X + p1.Y * p1.Y;
        var dC = p2.X * p2.X + p2.Y * p2.Y;

        var aux1 = (dA * (p2.Y - p1.Y) + dB * (p0.Y - p2.Y) + dC * (p1.Y - p0.Y));
        var aux2 = -(dA * (p2.X - p1.X) + dB * (p0.X - p2.X) + dC * (p1.X - p0.X));
        var div = (2 * (p0.X * (p2.Y - p1.Y) + p1.X * (p0.Y - p2.Y) + p2.X * (p1.Y - p0.Y)));

        if (div == 0)
        {
            //throw new DivideByZeroException();
            RadiusSquared = Double.PositiveInfinity;
            Circumcenter = p0;    
        }

        var center = new Point(aux1 / div, aux2 / div);
        Circumcenter = center;
        RadiusSquared = (center.X - p0.X) * (center.X - p0.X) + (center.Y - p0.Y) * (center.Y - p0.Y);
    }

    private bool IsCounterClockwise(Point point1, Point point2, Point point3)
    {
        var result = (point2.X - point1.X) * (point3.Y - point1.Y) -
                     (point3.X - point1.X) * (point2.Y - point1.Y);
        return result > 0;
    }

    public bool SharesEdgeWith(Triangle triangle)
    {
        var sharedVertices = Vertices.Where(o => triangle.Vertices.Contains(o)).Count();
        return sharedVertices == 2;
    }

    public bool IsPointInsideCircumcircle(Point point)
    {
        var d_squared = (point.X - Circumcenter.X) * (point.X - Circumcenter.X) +
                        (point.Y - Circumcenter.Y) * (point.Y - Circumcenter.Y);
        return d_squared < RadiusSquared;
    }
}