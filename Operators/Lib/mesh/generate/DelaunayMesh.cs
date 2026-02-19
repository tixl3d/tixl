using T3.Core.Rendering;
using T3.Core.Utils;

using DelaunatorSharp;



namespace Lib.mesh.generate;

[Guid("bf4daa46-ed0f-4a87-9ba1-93631b2ca29a")]
internal sealed class DelaunayMesh : Instance<DelaunayMesh>
{
    [Output(Guid = "6c85e367-f91c-4f3d-9d3d-e422a521e3a9")]
    public readonly Slot<MeshBuffers> Data = new();

    public DelaunayMesh()
    {
        Data.UpdateAction += Update;
    }

    

    private void Update(EvaluationContext context)
    {
        var pointList = BoundaryPoints.GetValue(context);
        var extraPointList = ExtraPoints.GetValue(context);
        var tweak = Tweak.GetValue(context);
        var seed = Seed.GetValue(context);
        var subdivideLongEdges = SubdivideLongEdges.GetValue(context);
        // Get fill density parameter
        // Note: Inverted so that 1 means full fill and 0 means no fill for the user
        var fillDensity = 1 - FillDensity.GetValue(context);
        try
        {
            // Get the point list from input
            
            if (pointList == null || pointList.NumElements == 0)
            {
                Log.Warning("DelaunayMesh: No points in list");
                return;
            }

            // Cast to StructuredList<Point> to access TypedElements
            var typedPointList = pointList as StructuredList<Point>;
            if (typedPointList == null)
            {
                Log.Error("DelaunayMesh: List is not of type StructuredList<Point>");
                return;
            }

            var originalPointArray = typedPointList.TypedElements;

            // Calculate hash of the boundary points array for cache invalidation
            int currentHash = CalculateBoundaryPointsHash(originalPointArray);
            

            

           // Determine if we need to reprocess the boundary points
            bool needsReprocessing = !_hasProcessedOnce ||                       // First evaluation after load (CRITICAL!)
                                      _cachedFilteredBoundaryPoints == null ||   // Cache empty                              
                                      currentHash != _cachedBoundaryPointsHash;  // Hash changed

            Point[] pointArray;

            if (needsReprocessing)
            {
                // Filter out points with NaN values
                var validPoints = new List<Point>();
                int invalidCount = 0;

                for (int i = 0; i < originalPointArray.Length; i++)
                {
                    var point = originalPointArray[i];
                    var scaleNaN = point.Scale;

                    // Check for NaN in Scale
                    if (float.IsNaN(scaleNaN.X) || float.IsNaN(scaleNaN.Y) || float.IsNaN(scaleNaN.Z))
                    {
                        invalidCount++;
                        continue;
                    }

                    validPoints.Add(point);
                }

                if (invalidCount > 0)
                {
                    Log.Debug($"DelaunayMesh: Filtered out {invalidCount} points with NaN Scale values");
                }

                pointArray = [.. validPoints];

                // Update cache
                _cachedFilteredBoundaryPoints = pointArray;
                _cachedBoundaryPointsHash = currentHash;
                _hasProcessedOnce = true;
            }
            else
            {
                // Use cached filtered points
                pointArray = _cachedFilteredBoundaryPoints;
                // Removed the debug log that was spamming the console
            }

            if (pointArray.Length < 3)
            {
                Log.Warning("DelaunayMesh: Need at least 3 valid points for triangulation");
                return;
            }

            

            

            // Subdivide long boundary edges for better triangulation
            var subdividedPoints = new List<Point>();
            var maxEdgeSubdivisionLength = 0f;
            if (subdivideLongEdges > 0)
            {
                maxEdgeSubdivisionLength = 1 - subdivideLongEdges; // Subdivide edges longer than 1.5x the fill density
            }


            for (int i = 0; i < pointArray.Length; i++)
            {
                var currentPoint = pointArray[i];
                var nextPoint = pointArray[(i + 1) % pointArray.Length];

                subdividedPoints.Add(currentPoint);

                // Calculate edge length
                var edgeLength = Vector2.Distance(
                    new Vector2(currentPoint.Position.X, currentPoint.Position.Y),
                    new Vector2(nextPoint.Position.X, nextPoint.Position.Y));

                // Subdivide if edge is too long
                if (edgeLength > maxEdgeSubdivisionLength && maxEdgeSubdivisionLength > 0.0001f)
                {
                    int subdivisions = (int)Math.Ceiling(edgeLength / maxEdgeSubdivisionLength);

                    for (int j = 1; j < subdivisions; j++)
                    {
                        float t = (float)j / subdivisions;
                        var interpolatedPos = Vector3.Lerp(currentPoint.Position, nextPoint.Position, t);
                        var interpolatedColor = Vector4.Lerp(currentPoint.Color, nextPoint.Color, t);
                        subdividedPoints.Add(new Point
                        {
                            Position = interpolatedPos,
                            F1 = 1,
                            Orientation = Quaternion.Identity,
                            Color = interpolatedColor,
                            Scale = Vector3.One,
                            F2 = 1
                        });
                    }
                }
            }

            pointArray = subdividedPoints.ToArray();

            // Store original boundary points before adding fill points
            var originalBoundaryCount = pointArray.Length;
            var boundaryPolygon = new Vector2[originalBoundaryCount];
            for (int i = 0; i < originalBoundaryCount; i++)
            {
                boundaryPolygon[i] = new Vector2(pointArray[i].Position.X, pointArray[i].Position.Y);
            }

            // Create spatial grid for boundary points
            float gridCellSize = fillDensity * 2f; // Adjust based on your needs
            var boundaryGrid = new BoundaryGrid(boundaryPolygon, gridCellSize);

            // Generate additional points inside the boundary using Poisson disc sampling
            var allPoints = new List<Point>(pointArray);

            // FIXED: Changed condition to check if fillDensity is above minimum threshold
            if (fillDensity < 0.9999f)
            {
                // Calculate bounds for the boundary using a single loop (more efficient than LINQ)
                var minXb = float.MaxValue;
                var maxXb = float.MinValue;
                var minYb = float.MaxValue;
                var maxYb = float.MinValue;

                for (int i = 0; i < pointArray.Length; i++)
                {
                    var pos = pointArray[i].Position;
                    if (pos.X < minXb) minXb = pos.X;
                    if (pos.X > maxXb) maxXb = pos.X;
                    if (pos.Y < minYb) minYb = pos.Y;
                    if (pos.Y > maxYb) maxYb = pos.Y;
                }


                // Generate Poisson disc samples with seed
                var fillPoints = GeneratePoissonDiscSamples(minXb, maxXb, minYb, maxYb, fillDensity, seed);

                // Filter out points that are:
                // 1. Not inside the boundary polygon
                // 2. Too close to boundary points (to avoid edge artifacts)
                var minDistanceFromBoundary = fillDensity * tweak; // Keep some margin from boundary
                var validFillPoints = new List<Vector2>();

                foreach (var fillPoint in fillPoints)
                {
                    // Check if inside boundary (still need polygon test)
                    if (!IsPointInPolygon(fillPoint, boundaryPolygon))
                        continue;

                    // Use spatial grid for distance check (much faster!)
                    if (!boundaryGrid.IsTooCloseToBoundary(fillPoint, minDistanceFromBoundary, boundaryPolygon))
                    {
                        validFillPoints.Add(fillPoint);
                    }
                }

                // Add valid fill points to the point array
                foreach (var fillPoint in validFillPoints)
                {
                    allPoints.Add(new Point
                    {
                        Position = new Vector3(fillPoint.X, fillPoint.Y, 0),
                        F1 = 1,
                        Orientation = Quaternion.Identity,
                        Color = Vector4.One,
                        Scale = Vector3.One,
                        F2 = 1
                    });
                }
            }
            else
            {
                // Use ExtraPoints list when fillDensity is very low (user's FillDensity > 0.9999)
                if (extraPointList != null && extraPointList.NumElements > 0)
                {
                    // Cast to StructuredList<Point> to access TypedElements
                    var typedExtraPointList = extraPointList as StructuredList<Point>;
                    if (typedExtraPointList != null)
                    {
                        var extraPointArray = typedExtraPointList.TypedElements;

                        // Filter extra points similarly to Poisson disc samples
                        var minDistanceFromBoundary = fillDensity * tweak; // Use same margin as for fill points

                        // Add valid extra points to the allPoints list
                        for (int i = 0; i < extraPointArray.Length; i++)
                        {
                            var point = extraPointArray[i];

                            // Skip points with NaN values
                            if (float.IsNaN(point.Scale.X) || float.IsNaN(point.Scale.Y) || float.IsNaN(point.Scale.Z))
                                continue;

                            var pointPos2D = new Vector2(point.Position.X, point.Position.Y);
                          

                            // Check if point is inside boundary polygon
                            if (!IsPointInPolygon(pointPos2D, boundaryPolygon))
                                continue;

                            // Check if point is too close to boundary (using the spatial grid for efficiency)
                            if (boundaryGrid.IsTooCloseToBoundary(pointPos2D, minDistanceFromBoundary, boundaryPolygon))
                                continue;

                            allPoints.Add(point);
                        }
                    }
                    else
                    {
                        Log.Warning("DelaunayMesh: ExtraPoints list is not of type StructuredList<Point>");
                    }
                }
            }

            // Use the combined point array for triangulation
            pointArray = allPoints.ToArray();


            // Convert Point array to IPoint array for Delaunator (only x,y coordinates)
            var delaunatorPoints = pointArray.Select(p => new DelaunatorSharp.Point(p.Position.X, p.Position.Y) as IPoint).ToArray();

            // Perform Delaunay triangulation
            var delaunay = new Delaunator(delaunatorPoints);
       
            // Get vertices and triangles count
            var verticesCount = pointArray.Length;
            var triangleCount = delaunay.Triangles.Length / 3;

            // Calculate bounds for UV mapping
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;

            foreach (var point in pointArray)
            {
                var x = point.Position.X;
                var y = point.Position.Y;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            var rangeX = maxX - minX;
            var rangeY = maxY - minY;

            // Avoid division by zero for UV calculation
            if (rangeX < 0.0001f) rangeX = 1.0f;
            if (rangeY < 0.0001f) rangeY = 1.0f;

            // Create vertices with transformations
            if (_vertexBufferData.Length != verticesCount)
            {
                _vertexBufferData = new PbrVertex[verticesCount];
            }

            for (int i = 0; i < verticesCount; i++)
            {
                var point = pointArray[i];
                var pos = point.Position;
                var color = new Vector3( point.Color.X, point.Color.Y, point.Color.Z);

                // Calculate UV coordinates (normalized 0-1 based on point positions)
                var u = (pos.X - minX) / rangeX;
                var v = (pos.Y - minY) / rangeY;
                var uv = new Vector2(u, v);

                _vertexBufferData[i] = new PbrVertex
                {
                    Position = pos,
                    Normal = new Vector3(0, 0, 1),
                    Tangent = new Vector3(1, 0, 0),
                    Bitangent = new Vector3(0, 1, 0),
                    Texcoord = uv,
                    Selection = point.F1,
                    ColorRgb = color,
                };
            }

            // Filter triangles based on edge length and boundary
            var validTriangles = new List<Int3>();
           

            for (int i = 0; i < triangleCount; i++)
            {
                var idx0 = delaunay.Triangles[i * 3];
                var idx1 = delaunay.Triangles[i * 3 + 1];
                var idx2 = delaunay.Triangles[i * 3 + 2];

                var p0 = pointArray[idx0].Position;
                var p1 = pointArray[idx1].Position;
                var p2 = pointArray[idx2].Position;

                bool keepTriangle = true;

                // Boundary filtering - check if triangle centroid is inside boundary polygon
                if (keepTriangle)
                {
                    var centroid = new Vector2(
                        (p0.X + p1.X + p2.X) / 3f,
                        (p0.Y + p1.Y + p2.Y) / 3f
                    );

                    if (!IsPointInPolygon(centroid, boundaryPolygon))
                    {
                        keepTriangle = false;
                    }
                }

                if (keepTriangle)
                {
                    validTriangles.Add(new Int3(idx0, idx2, idx1)); // Reversed winding order for Tixl's back-face culling
                }
            }

            var faceCount = validTriangles.Count;

            // Create index buffer
            if (_indexBufferData.Length != faceCount)
            {
                _indexBufferData = new Int3[faceCount];
            }

            for (int i = 0; i < faceCount; i++)
            {
                _indexBufferData[i] = validTriangles[i];
            }

            // Update GPU buffers
            var stride = PbrVertex.Stride;
            ResourceManager.SetupStructuredBuffer(_vertexBufferData, stride * verticesCount, stride, ref _vertexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_vertexBuffer, ref _vertexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_vertexBuffer, UnorderedAccessViewBufferFlags.None, ref _vertexBufferWithViews.Uav);
            _vertexBufferWithViews.Buffer = _vertexBuffer;

            stride = 3 * sizeof(int);
            ResourceManager.SetupStructuredBuffer(_indexBufferData, stride * faceCount, stride, ref _indexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_indexBuffer, ref _indexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_indexBuffer, UnorderedAccessViewBufferFlags.None, ref _indexBufferWithViews.Uav);
            _indexBufferWithViews.Buffer = _indexBuffer;

            _data.VertexBuffer = _vertexBufferWithViews;
            _data.IndicesBuffer = _indexBufferWithViews;
            Data.Value = _data;
            Data.DirtyFlag.Clear();
        }
        catch (Exception e)
        {
            Log.Error("Failed to create Delaunay mesh: " + e.Message);
        }
    }

    // Point-in-polygon test using ray casting algorithm
    private static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
    {
        var inside = false;
        var n = polygon.Length;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    // Generate Poisson disc samples inside the boundary polygon
    private static List<Vector2> GeneratePoissonDiscSamples(float minX, float maxX, float minY, float maxY, float radius, int seed)
    {
        var samples = new List<Vector2>();
        var activeList = new List<Vector2>();
        var random = new System.Random(seed); // FIXED: Use seed for deterministic generation

        // Grid cell size
        var cellSize = radius / MathF.Sqrt(2);
        var gridWidth = (int)MathF.Ceiling((maxX - minX) / cellSize);
        var gridHeight = (int)MathF.Ceiling((maxY - minY) / cellSize);

        // Grid to track occupied cells (-1 = empty, >= 0 = index in samples list)
        var grid = new int[gridWidth * gridHeight];
        for (int i = 0; i < grid.Length; i++)
            grid[i] = -1;

        // Helper function to get grid index
        int GetGridIndex(float x, float y)
        {
            int gridX = (int)((x - minX) / cellSize);
            int gridY = (int)((y - minY) / cellSize);
            if (gridX < 0 || gridX >= gridWidth || gridY < 0 || gridY >= gridHeight)
                return -1;
            return gridX + gridY * gridWidth;
        }

        // Start with the center point of the rectangle
        Vector2 firstPoint = new Vector2(
            minX + (maxX - minX) * 0.5f,
            minY + (maxY - minY) * 0.5f
        );

        samples.Add(firstPoint);
        activeList.Add(firstPoint);

        int gridIdx = GetGridIndex(firstPoint.X, firstPoint.Y);
        if (gridIdx >= 0)
            grid[gridIdx] = 0;

        // Process active list
        int maxAttempts = 30; // Standard Poisson disc parameter

        while (activeList.Count > 0)
        {
            int randomIndex = random.Next(activeList.Count);
            Vector2 point = activeList[randomIndex];
            bool foundCandidate = false;

            for (int i = 0; i < maxAttempts; i++)
            {
                // Generate random point around the active point
                float angle = (float)(random.NextDouble() * 2 * MathF.PI);
                float distance = radius + (float)(random.NextDouble() * radius);

                float newX = point.X + distance * MathF.Cos(angle);
                float newY = point.Y + distance * MathF.Sin(angle);
                var newPoint = new Vector2(newX, newY);

                // Check if point is within bounds and inside boundary
                if (newX < minX || newX >= maxX || newY < minY || newY >= maxY)
                    continue;

                // Check if point is far enough from all other points
                int newGridIdx = GetGridIndex(newX, newY);
                if (newGridIdx < 0)
                    continue;

                bool tooClose = false;

                // Pre-calculate squared radius (avoid square root in distance checks)
                float radiusSquared = radius * radius;

                // Check neighboring grid cells
                int gridX = (int)((newX - minX) / cellSize);
                int gridY = (int)((newY - minY) / cellSize);

                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int checkX = gridX + dx;
                        int checkY = gridY + dy;

                        if (checkX < 0 || checkX >= gridWidth || checkY < 0 || checkY >= gridHeight)
                            continue;

                        int checkIdx = checkX + checkY * gridWidth;
                        int sampleIdx = grid[checkIdx];

                        if (sampleIdx >= 0)
                        {
                            Vector2 sample = samples[sampleIdx];
                            float dxVal = newPoint.X - sample.X;
                            float dyVal = newPoint.Y - sample.Y;
                            float distSquared = dxVal * dxVal + dyVal * dyVal;

                            if (distSquared < radiusSquared)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                    }
                    if (tooClose) break;
                }

                if (!tooClose)
                {
                    samples.Add(newPoint);
                    activeList.Add(newPoint);
                    grid[newGridIdx] = samples.Count - 1;
                    foundCandidate = true;
                    break;
                }
            }

            if (!foundCandidate)
            {
                activeList.RemoveAt(randomIndex);
            }
        }

        return samples;
    }

    // Calculate a simple hash of the boundary points array for cache invalidation
    private static int CalculateBoundaryPointsHash(Point[] points)
    {
        if (points == null || points.Length == 0)
            return 0;

        unchecked
        {
            int hash = 17;
            hash = hash * 31 + points.Length;

            // Sample points for hash calculation to avoid performance issues with large arrays
            // Hash first, middle, and last points, plus array length
            if (points.Length > 0)
            {
                hash = hash * 31 + HashPoint(points[0]);
            }
            if (points.Length > 1)
            {
                hash = hash * 31 + HashPoint(points[points.Length - 1]);
            }
            if (points.Length > 2)
            {
                hash = hash * 31 + HashPoint(points[points.Length / 2]);
            }

            return hash;
        }
    }

    // Helper to hash a single Point's key properties
    private static int HashPoint(Point point)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + point.Position.GetHashCode();
            hash = hash * 31 + point.Scale.GetHashCode();
            return hash;
        }
    }

   
    private class BoundaryGrid
    {
        private readonly float _minX, _minY, _maxX, _maxY;
        private readonly float _cellSize;
        private readonly int _gridWidth, _gridHeight;
        private readonly List<int>[,] _grid; // Stores indices of boundary points in each cell

        public BoundaryGrid(Vector2[] boundaryPoints, float cellSize)
        {
            _cellSize = cellSize;

            // Calculate bounds
            _minX = float.MaxValue;
            _minY = float.MaxValue;
            _maxX = float.MinValue;
            _maxY = float.MinValue;

            foreach (var point in boundaryPoints)
            {
                if (point.X < _minX) _minX = point.X;
                if (point.X > _maxX) _maxX = point.X;
                if (point.Y < _minY) _minY = point.Y;
                if (point.Y > _maxY) _maxY = point.Y;
            }

            // Add small padding to handle points exactly on bounds
            _minX -= 0.001f;
            _minY -= 0.001f;
            _maxX += 0.001f;
            _maxY += 0.001f;

            _gridWidth = (int)Math.Ceiling((_maxX - _minX) / _cellSize);
            _gridHeight = (int)Math.Ceiling((_maxY - _minY) / _cellSize);

            // Initialize grid
            _grid = new List<int>[_gridWidth, _gridHeight];
            for (int x = 0; x < _gridWidth; x++)
                for (int y = 0; y < _gridHeight; y++)
                    _grid[x, y] = new List<int>();

            // Add points to grid
            for (int i = 0; i < boundaryPoints.Length; i++)
            {
                var point = boundaryPoints[i];
                int gridX = (int)((point.X - _minX) / _cellSize);
                int gridY = (int)((point.Y - _minY) / _cellSize);

                // Clamp to grid bounds (shouldn't happen with padding)
                gridX = Math.Clamp(gridX, 0, _gridWidth - 1);
                gridY = Math.Clamp(gridY, 0, _gridHeight - 1);

                _grid[gridX, gridY].Add(i);
            }
        }

        // Check if a point is too close to any boundary point
        public bool IsTooCloseToBoundary(Vector2 point, float minDistance, Vector2[] boundaryPoints)
        {
            float minDistanceSquared = minDistance * minDistance;

            // Determine which grid cell this point falls into
            int gridX = (int)((point.X - _minX) / _cellSize);
            int gridY = (int)((point.Y - _minY) / _cellSize);

            // Check neighboring cells (including current cell)
            int startX = Math.Max(0, gridX - 1);
            int endX = Math.Min(_gridWidth - 1, gridX + 1);
            int startY = Math.Max(0, gridY - 1);
            int endY = Math.Min(_gridHeight - 1, gridY + 1);

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    foreach (int index in _grid[x, y])
                    {
                        Vector2 boundaryPoint = boundaryPoints[index];
                        float dx = point.X - boundaryPoint.X;
                        float dy = point.Y - boundaryPoint.Y;
                        float distSquared = dx * dx + dy * dy;

                        if (distSquared < minDistanceSquared)
                            return true;
                    }
                }
            }

            return false;
        }
    }

    private Buffer _vertexBuffer;
    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private readonly BufferWithViews _vertexBufferWithViews = new();

    private Buffer _indexBuffer;
    private Int3[] _indexBufferData = new Int3[0];
    private readonly BufferWithViews _indexBufferWithViews = new();

    private readonly MeshBuffers _data = new();

    // Caching for NaN-filtered boundary points
    private Point[] _cachedFilteredBoundaryPoints = null;
    private int _cachedBoundaryPointsHash = 0;
    private bool _hasProcessedOnce = false;

    [Input(Guid = "18FDDD63-DB79-4EE6-9A32-B90A5CEFF582")]
    public readonly InputSlot<StructuredList> BoundaryPoints = new();

    [Input(Guid = "DB3C69B1-403B-485B-94E8-FC7E8B566947")]
    public readonly InputSlot<StructuredList> ExtraPoints = new();

    [Input(Guid = "ABA31520-065F-40C7-A4A6-A4470F1E0CDF")]
    public readonly InputSlot<float> SubdivideLongEdges = new();

    [Input(Guid = "e00e4b12-8576-4a78-b773-17630b102a70")]
    public readonly InputSlot<float> FillDensity = new();

    [Input(Guid = "3236E937-9DBE-41E8-AAFA-C0C13C56BCDF")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "0B30E8F2-44D7-41DB-B38B-E6A053B1AEBA")]
    public readonly InputSlot<float> Tweak = new();

}