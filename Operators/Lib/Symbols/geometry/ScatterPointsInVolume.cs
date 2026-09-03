namespace Lib.geometry;

/// <summary>
/// Scatters points deterministically inside a box volume or a MeshGeometry.
/// An optional ScalarField modulates the density via rejection sampling
/// (values clamped to 0..1), so dense and sparse regions can be sculpted.
/// </summary>
[Guid("9a51e3c7-4d08-4b62-bf35-1c7a8e9d0f46")]
internal sealed class ScatterPointsInVolume : Instance<ScatterPointsInVolume>
{
    [Output(Guid = "5c0b7d29-8ae4-4f13-96d7-e2a94c6b8503")]
    public readonly Slot<StructuredList> ResultList = new();

    public ScatterPointsInVolume()
    {
        ResultList.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var count = Math.Clamp(Count.GetValue(context), 0, 1_000_000);
        var center = Center.GetValue(context);
        var size = Size.GetValue(context);
        var density = Density.GetValue(context);
        var seed = Seed.GetValue(context);
        var geometry = Geometry.GetValue(context);

        var boundsMin = center - size * 0.5f;
        var boundsMax = center + size * 0.5f;
        if (geometry != null && geometry.Positions.Length > 0)
        {
            boundsMin = new Vector3(float.MaxValue);
            boundsMax = new Vector3(float.MinValue);
            foreach (var p in geometry.Positions)
            {
                boundsMin = Vector3.Min(boundsMin, p);
                boundsMax = Vector3.Max(boundsMax, p);
            }
        }

        var extent = boundsMax - boundsMin;
        var rngState = (uint)(seed * 747796405 + 2891336453);

        var accepted = 0;
        var maxAttempts = (long)count * 50 + 100;
        EnsureCapacity(count);

        for (long attempt = 0; attempt < maxAttempts && accepted < count; attempt++)
        {
            var position = boundsMin + new Vector3(NextFloat(ref rngState) * extent.X,
                                                   NextFloat(ref rngState) * extent.Y,
                                                   NextFloat(ref rngState) * extent.Z);

            if (geometry != null && !IsInsideMesh(geometry, position))
                continue;

            if (density != null)
            {
                var acceptance = Math.Clamp(density.Sample(position), 0f, 1f);
                if (NextFloat(ref rngState) >= acceptance)
                    continue;
            }

            _pointList[accepted++] = new Point
                                         {
                                             Position = position,
                                             F1 = 1,
                                             F2 = 0,
                                             Scale = Vector3.One,
                                             Orientation = Quaternion.Identity,
                                             Color = Vector4.One,
                                         };
        }

        if (_pointList.NumElements != accepted)
            _pointList.SetLength(accepted);

        ResultList.Value = _pointList;
    }

    private void EnsureCapacity(int count)
    {
        if (_pointList.NumElements != count)
            _pointList.SetLength(count);
    }

    /// <summary>Ray-parity test along +X against the fan-triangulated faces.</summary>
    private static bool IsInsideMesh(MeshGeometry geometry, Vector3 position)
    {
        var positions = geometry.Positions;
        var offsets = geometry.FaceCornerOffsets;
        var corners = geometry.CornerPointIndices;
        var crossings = 0;

        for (var faceIndex = 0; faceIndex < geometry.FaceCount; faceIndex++)
        {
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            for (var c = start + 2; c < end; c++)
            {
                if (RayIntersectsTriangle(position,
                                          positions[corners[start]],
                                          positions[corners[c - 1]],
                                          positions[corners[c]]))
                {
                    crossings++;
                }
            }
        }

        return (crossings & 1) == 1;
    }

    /// <summary>Möller-Trumbore for a ray along +X.</summary>
    private static bool RayIntersectsTriangle(Vector3 origin, Vector3 a, Vector3 b, Vector3 c)
    {
        var edge1 = b - a;
        var edge2 = c - a;

        // rayDir = (1,0,0): cross(rayDir, edge2) = (0, -edge2.Z, edge2.Y)
        var px = 0f;
        var py = -edge2.Z;
        var pz = edge2.Y;
        var det = edge1.X * px + edge1.Y * py + edge1.Z * pz;
        if (MathF.Abs(det) < 1e-10f)
            return false;

        var invDet = 1f / det;
        var t = origin - a;
        var u = (t.X * px + t.Y * py + t.Z * pz) * invDet;
        if (u < 0f || u > 1f)
            return false;

        // q = cross(t, edge1)
        var qx = t.Y * edge1.Z - t.Z * edge1.Y;
        var qy = t.Z * edge1.X - t.X * edge1.Z;
        var qz = t.X * edge1.Y - t.Y * edge1.X;
        var v = qx * invDet; // dot(rayDir, q) with rayDir = (1,0,0)
        if (v < 0f || u + v > 1f)
            return false;

        var distance = (edge2.X * qx + edge2.Y * qy + edge2.Z * qz) * invDet;
        return distance > 1e-8f;
    }

    private static float NextFloat(ref uint state)
    {
        // PCG-style output permutation on an LCG state
        state = state * 747796405 + 2891336453;
        var word = ((state >> (int)((state >> 28) + 4)) ^ state) * 277803737;
        word = (word >> 22) ^ word;
        return word / 4294967296f;
    }

    private readonly StructuredList<Point> _pointList = new(16);

    [Input(Guid = "e17d4a92-6b58-4c03-a9f1-84c5d2e7b360")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "3f82c6b1-9e07-45da-b824-56a1f9d3c708")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "a94b0e58-27c3-4f61-90ad-c8e52b7f1d39")]
    public readonly InputSlot<Vector3> Size = new();

    [Input(Guid = "58c1f7a3-b2d9-4e06-85cb-7f4a90e6d221")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "c6a35d18-40fb-4972-bde6-19f8c2a7e054")]
    public readonly InputSlot<ScalarField> Density = new();

    [Input(Guid = "12e9b8c4-75a0-4d36-9f82-3dc6e1b0a597")]
    public readonly InputSlot<int> Seed = new();
}
