#nullable enable
using Lib.Utils;

namespace Lib.geometry.modify;

/// <summary>
/// Welds points that lie within a tolerance into one point and remaps the vertices of polygons and curves to it, e.g. to
/// join the duplicated positions of an imported mesh. The merged point keeps the position and the attribute values of the
/// first point of its group, so welding curve points with different handles changes the curves; points that end up without
/// a vertex are dropped. Primitive topology, parts and the vertex, primitive and part attributes are left unchanged.
///
/// The position-level counterpart of Blender's merge by distance. [GeometryToMesh] emits one vertex per face corner, so a
/// compiled mesh can still contain duplicate vertices even when its points are welded; its Merge Duplicate Vertices option
/// removes those.
/// </summary>
[Guid("12492a88-9e16-49a4-84c7-75ab11bb7bd4")]
internal sealed class MergeVertices : Instance<MergeVertices>, IStatusProvider
{
    [Output(Guid = "0fe84b04-eacd-4e51-8932-bc673e86d07e")]
    public readonly Slot<Geometry?> Result = new();

    public MergeVertices()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var tolerance = MathF.Max(Tolerance.GetValue(context), 0);
        if (source == null || source.PointCount < 2)
        {
            _statusMessage = string.Empty;
            Result.Value = source;
            return;
        }

        var removedPoints = Weld(source, tolerance);
        if (removedPoints == 0)
        {
            _statusMessage = "No points to merge within the tolerance.";
            Result.Value = source;
            return;
        }

        _statusMessage = $"Merged {removedPoints:N0} of {source.PointCount:N0} points.";
        Result.Value = _output;
    }

    /// <summary>Groups the points within the tolerance and writes the welded geometry into <see cref="_output"/>.</summary>
    /// <returns>How many points were removed.</returns>
    private int Weld(Geometry source, float tolerance)
    {
        var positions = source.Positions;
        var pointCount = positions.Length;
        EnsureLength(ref _representative, pointCount);
        EnsureLength(ref _nextPointInCell, pointCount);
        EnsureLength(ref _hasVertices, pointCount);
        EnsureLength(ref _outputIndex, pointCount);

        // A point joins the nearest already kept point within reach; the grid looks one cell further in each direction
        var cellSize = MathF.Max(tolerance, 1e-6f);
        var inverseCellSize = 1f / cellSize;
        var toleranceSq = tolerance * tolerance;
        _cellHeads.Clear();

        var removedPoints = 0;
        for (var point = 0; point < pointCount; point++)
        {
            var position = positions[point];
            var cellX = (long)MathF.Floor(position.X * inverseCellSize);
            var cellY = (long)MathF.Floor(position.Y * inverseCellSize);
            var cellZ = (long)MathF.Floor(position.Z * inverseCellSize);

            var nearest = -1;
            var nearestDistanceSq = toleranceSq;
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (var offsetZ = -1; offsetZ <= 1; offsetZ++)
                    {
                        if (!_cellHeads.TryGetValue(CellKey(cellX + offsetX, cellY + offsetY, cellZ + offsetZ), out var candidate))
                            continue;

                        while (candidate >= 0)
                        {
                            var distanceSq = Vector3.DistanceSquared(positions[candidate], position);
                            if (distanceSq <= nearestDistanceSq)
                            {
                                nearestDistanceSq = distanceSq;
                                nearest = candidate;
                            }

                            candidate = _nextPointInCell[candidate];
                        }
                    }
                }
            }

            if (nearest >= 0)
            {
                _representative[point] = nearest;
                removedPoints++;
                continue;
            }

            _representative[point] = point;
            var key = CellKey(cellX, cellY, cellZ);
            _nextPointInCell[point] = _cellHeads.GetValueOrDefault(key, -1);
            _cellHeads[key] = point;
        }

        if (removedPoints == 0)
            return 0;

        // Only points that a vertex still references survive; the rest are the welded duplicates
        Array.Clear(_hasVertices, 0, pointCount);
        var vertexPoints = source.VertexPoints;
        for (var vertex = 0; vertex < vertexPoints.Length; vertex++)
            _hasVertices[_representative[vertexPoints[vertex]]] = true;

        var outputPointCount = 0;
        for (var point = 0; point < pointCount; point++)
            _outputIndex[point] = _hasVertices[point] ? outputPointCount++ : -1;

        var primitiveCount = source.PrimitiveCount;
        var vertexCount = vertexPoints.Length;
        _output.Resize(outputPointCount, primitiveCount, vertexCount);
        Array.Copy(source.PrimitiveVertexOffsets, _output.PrimitiveVertexOffsets, primitiveCount + 1);
        Array.Copy(source.PrimitiveKind, _output.PrimitiveKind, primitiveCount);
        Array.Copy(source.PrimitiveClosed, _output.PrimitiveClosed, primitiveCount);
        _output.Parts = source.Parts;

        for (var vertex = 0; vertex < vertexCount; vertex++)
            _output.VertexPoints[vertex] = _outputIndex[_representative[vertexPoints[vertex]]];

        for (var point = 0; point < pointCount; point++)
        {
            if (_hasVertices[point])
                _output.Positions[_outputIndex[point]] = positions[point];
        }

        // Point values follow their group's first point; domains whose element counts are unchanged are shared
        _pointBlend.Clear();
        for (var point = 0; point < pointCount; point++)
        {
            if (_hasVertices[point])
                _pointBlend.AddCopy(point);
        }

        _output.Attributes.Clear();
        _pointBlend.Apply(source.Attributes, _output.Attributes, AttributeDomain.Point);
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            if (attribute.Domain is AttributeDomain.Vertex or AttributeDomain.Primitive or AttributeDomain.Part)
                _output.Attributes.Add(attribute);
        }

        _output.InvalidateTopologyCaches();
        return removedPoints;
    }

    /** Hash of a grid cell; a collision only adds distance checks to the candidates it shares a bucket with. */
    private static long CellKey(long x, long y, long z)
    {
        unchecked
        {
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }
    }

    private static void EnsureLength<T>(ref T[] array, int length)
    {
        if (array.Length < length)
            array = new T[length];
    }

    public IStatusProvider.StatusLevel GetStatusLevel() => IStatusProvider.StatusLevel.Success;

    public string GetStatusMessage() => _statusMessage;

    private string _statusMessage = string.Empty;

    /** Per point: the point it was merged into, or itself. */
    private int[] _representative = [];

    /** Per point: the next kept point in the same grid cell, or -1. */
    private int[] _nextPointInCell = [];

    /** Per point: whether a vertex still references its group. */
    private bool[] _hasVertices = [];

    /** Per kept point: its index in the output, -1 for the others. */
    private int[] _outputIndex = [];

    private readonly Dictionary<long, int> _cellHeads = [];
    private readonly GeometryAttributeBlend _pointBlend = new();
    private readonly Geometry _output = new();

    [Input(Guid = "81b960fa-f826-481c-9b1e-40fb34fc6e62")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "00348b5b-f0dc-40aa-a8f2-1b0c6d03be99")]
    public readonly InputSlot<float> Tolerance = new();
}
