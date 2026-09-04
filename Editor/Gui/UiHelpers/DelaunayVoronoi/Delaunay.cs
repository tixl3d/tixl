namespace T3.Editor.Gui.UiHelpers.DelaunayVoronoi;

public sealed class DelaunayTriangulator
{
    private double MaxX { get; set; }
    private double MaxY { get; set; }
    private IEnumerable<Triangle> _border;

    public IEnumerable<Point> GeneratePoints(int amount, double maxX, double maxY)
    {
        MaxX = maxX;
        MaxY = maxY;

        // TODO make more beautiful
        var point0 = new Point(0, 0);
        var point1 = new Point(0, MaxY);
        var point2 = new Point(MaxX, MaxY);
        var point3 = new Point(MaxX, 0);
        var points = new List<Point>() { point0, point1, point2, point3 };
        var tri1 = new Triangle(point0, point1, point2);
        var tri2 = new Triangle(point0, point2, point3);
        _border = new List<Triangle>() { tri1, tri2 };

        var random = new Random();
        for (int i = 0; i < amount - 4; i++)
        {
            var pointX = random.NextDouble() * MaxX;
            var pointY = random.NextDouble() * MaxY;
            points.Add(new Point(pointX, pointY));
        }

        return points;
    }

    public List<Point> SetBorder(Point min, Point max)
    {
        // TODO make more beautiful
        var point0 = new Point(min.X, min.Y);
        var point1 = new Point(max.X, min.Y);
        var point2 = new Point(max.X, max.Y);
        var point3 = new Point(min.X, max.Y);
        var tri1 = new Triangle(point0, point1, point2);
        var tri2 = new Triangle(point0, point2, point3);
        _border = new List<Triangle>() { tri1, tri2 };
        return new List<Point>() { point0, point1, point2, point3 };
            
    }

    public IEnumerable<Triangle> BowyerWatson(IEnumerable<Point> points)
    {
        //var supraTriangle = GenerateSupraTriangle();
        var triangulation = new HashSet<Triangle>(_border);

        foreach (var point in points)
        {
            var badTriangles = FindBadTriangles(point, triangulation);
            var polygon = FindHoleBoundaries(badTriangles);

            foreach (var triangle in badTriangles)
            {
                foreach (var vertex in triangle.Vertices)
                {
                    vertex.AdjacentTriangles.Remove(triangle);
                }
            }
            triangulation.RemoveWhere(o => badTriangles.Contains(o));

            foreach (var edge in polygon.Where(possibleEdge => possibleEdge.Point1 != point && possibleEdge.Point2 != point))
            {
                var triangle = new Triangle(point, edge.Point1, edge.Point2);
                triangulation.Add(triangle);
            }
        }

        //triangulation.RemoveWhere(o => o.Vertices.Any(v => supraTriangle.Vertices.Contains(v)));
        return triangulation;
    }

    private List<Edge> FindHoleBoundaries(ISet<Triangle> badTriangles)
    {
        var edges = new List<Edge>();
        foreach (var triangle in badTriangles)
        {
            edges.Add(new Edge(triangle.Vertices[0], triangle.Vertices[1]));
            edges.Add(new Edge(triangle.Vertices[1], triangle.Vertices[2]));
            edges.Add(new Edge(triangle.Vertices[2], triangle.Vertices[0]));
        }
        var grouped = edges.GroupBy(o => o);
        var boundaryEdges = edges.GroupBy(o => o).Where(o => o.Count() == 1).Select(o => o.First());
        return boundaryEdges.ToList();
    }

    private Triangle GenerateSupraTriangle()
    {
        //   1  -> maxX
        //  / \
        // 2---3
        // |
        // v maxY
        var margin = 500;
        var point1 = new Point(0.5 * MaxX, -2 * MaxX - margin);
        var point2 = new Point(-2 * MaxY - margin, 2 * MaxY + margin);
        var point3 = new Point(2 * MaxX + MaxY + margin, 2 * MaxY + margin);
        return new Triangle(point1, point2, point3);
    }

    private ISet<Triangle> FindBadTriangles(Point point, HashSet<Triangle> triangles)
    {
        var badTriangles = triangles.Where(o => o.IsPointInsideCircumcircle(point));
        return new HashSet<Triangle>(badTriangles);
    }
}